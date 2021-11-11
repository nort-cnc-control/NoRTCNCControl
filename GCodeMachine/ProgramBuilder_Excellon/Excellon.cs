using System;
using System.Collections.Generic;
using System.Linq;

namespace Excellon
{
	public class ProgramBuilder_Excellon : ILoggerSource
	{
		private readonly MachineParameters config;

		private readonly ProgramBuilder builder;

		public string Name => "excellon builder";

		private readonly IRTSender rtSender;
		private readonly IModbusSender modbusSender;
		private readonly IMachine machine;
		private readonly IMillManager toolManager;

		public ProgramBuilder_Excellon(ProgramBuilder builder,
									IMachine machine,
									IMillManager toolManager,
									MachineParameters config,
									IRTSender rtSender,
									IModbusSender modbusSender)
		{
			this.builder = builder;
			this.machine = machine;
			this.toolManager = toolManager;
			this.config = config;
			this.rtSender = rtSender;
			this.modbusSender = modbusSender;
		}

		private decimal? GetValue(Arguments.Option option, CNCState.CNCState state)
		{
			if (option == null)
				return null;
			if (option.type == Arguments.Option.ValueType.Expression)
				return option.expr.Evaluate(state.VarsState.Vars);
			return option.value;
		}

		private CNCState.CNCState ProcessDrillingMove(Arguments block,
													  ActionProgram.ActionProgram program,
													  CNCState.CNCState state)
		{
			// TODO Handle G17, 18, 19

			decimal? X = GetValue(block.X, state);
			decimal? Y = GetValue(block.Y, state);
			decimal? Z = GetValue(block.Z, state); // Drill depth
			decimal? R = GetValue(block.R, state); // Retract
			decimal? Q = GetValue(block.Q, state); // Pecking

			return builder.ProcessDrillingMove(X, Y, Z, R, Q, program, state);
		}

		private (CNCState.CNCState state,
				 ProgramBuilderCommand command,
				 int pid,
				 int amount)

			ProcessBlock(Arguments block,
						 ActionProgram.ActionProgram program,
						 CNCState.CNCState state)
		{
			var cmd = block.Options.FirstOrDefault((arg) => (arg.codeType == Arguments.Option.CodeType.Code && (arg.letter == 'G' || arg.letter == 'M')));
			
			int pid = -1, amount = -1;
			state = state.BuildCopy();

			foreach (var opt in block.Options)
			{
				if (opt.codeType == Arguments.Option.CodeType.Variable)
				{
					var val = GetValue(opt, state);
					string varid = opt.varid;
					state = state.BuildCopy();
					state.VarsState.Vars[varid] = val.Value;
				}
			}

			if (cmd == null)
			{
				state = ProcessDrillingMove(block, program, state);
			}
			else if (cmd.letter == 'G')
			{
				switch (cmd.ivalue1)
				{
					case 90:
						state.AxisState.Absolute = true;
						break;
					case 91:
						state.AxisState.Absolute = false;
						break;					
				}
			}
			else if (cmd.letter == 'M')
			{
				switch (cmd.ivalue1)
				{
					case 0:
						
						break;
					case 2:
						
						break;
					case 3:
					case 4:
					case 5:
						state = ProcessToolCommand(block, program, state);
						break;
					
					case 6:
						if (block.SingleOptions.ContainsKey('T'))
						{
							// TODO: stop spindle
							int tool = block.SingleOptions['T'].ivalue1;
							
						}
						break;
					case 97:
						command = ProgramBuilderCommand.Call;
						(pid, amount) = CallSubprogram(block, state);
						break;
					case 99:
						command = ProgramBuilderCommand.Return;
						break;
					case 120:
						builder.PushState(state.AxisState);
						break;
					case 121:
						builder.PopState(state.AxisState);
						break;
					case 703:
					case 705:
						state = ProcessSyncToolCommand(block, program, state);
						break;
				}
			}

			return (state, command, pid, amount);
		}

		private (CNCState.CNCState,
				 ProgramBuilderCommand command,
				 int programid,
				 int amount)

			Process(Arguments args,
					ActionProgram.ActionProgram program,
					CNCState.CNCState state,
					int curprogram, int curline,
					Dictionary<IAction, (int, int)> starts)
		{
			ProgramBuilderCommand command = ProgramBuilderCommand.Continue;
			int pid = -1, amount = -1;

			var line_number = args.LineNumber;
			var len0 = program.Actions.Count;
			var sargs = SplitFrame(args);
			foreach (var block in sargs)
			{
				(state, command, pid, amount) = ProcessBlock(block, program, state);
			}

			var len1 = program.Actions.Count;
			if (len1 > len0)
			{
				var (first, _, _) = program.Actions[len0];
				starts[first] = (curprogram, curline);
			}
			return (state, command, pid, amount);
		}

		public ProgramBuildingState InitNewProgram(ProgramSource source)
		{
			var builderState = new ProgramBuildingState(source)
			{
				CurrentProcedure = source.MainProcedureId,
				CurrentLine = 0
			};
			return builderState;
		}

		public (ActionProgram.ActionProgram actionProgram,
				ProgramBuildingState finalState,
				IReadOnlyDictionary<IAction, (int procedure, int line)> actionLines,
				string errorMessage)

			BuildProgram(CNCState.CNCState initialMachineState,
						 ProgramBuildingState builderState)
		{
			var program = new ActionProgram.ActionProgram(rtSender, modbusSender, config, machine, toolManager);
			var actionLines = new Dictionary<IAction, (int procedure, int line)>();
			Sequence sequence = builderState.Source.Procedures[builderState.CurrentProcedure];
			
			var state = builder.BeginProgram(program, initialMachineState);
			actionLines[program.Actions[0].action] = (-1, -1);
			bool finish = false;

			while (!finish)
			{
				Logger.Instance.Debug(this, "build", string.Format("processing line {0}", builderState.CurrentLine));
				if (builderState.CurrentLine >= sequence.Lines.Count)
				{
					builderState.Completed = true;
					break;
				}
				Arguments frame = sequence.Lines[builderState.CurrentLine];
				try
				{
					int newpid, amount;
					ProgramBuilderCommand command;
					(state, command, newpid, amount) = Process(frame,
															   program,
															   state,
															   builderState.CurrentProcedure,
															   builderState.CurrentLine,
															   actionLines);
					switch (command)
					{
						case ProgramBuilderCommand.Call:
							{
								if (amount > 0)
								{
									builderState.Callstack.Push((builderState.CurrentProcedure, builderState.CurrentLine, newpid, amount));
									builderState.CurrentProcedure = newpid;
									builderState.CurrentLine = 0;
									sequence = builderState.Source.Procedures[builderState.CurrentProcedure];
								}
								else
								{
									builderState.CurrentLine += 1;
								}
								break;
							}
						case ProgramBuilderCommand.Continue:
							{
								builderState.CurrentLine += 1;
								break;
							}
						case ProgramBuilderCommand.Return:
							{
								if (builderState.Callstack.Count == 0)
								{
									builderState.Completed = true;
									finish = true;
									break;
								}
								var top = builderState.Callstack.Pop();
								if (top.repeat == 1)
								{
									builderState.CurrentProcedure = top.fromProcedure;
									builderState.CurrentLine = top.fromLine + 1;
									sequence = builderState.Source.Procedures[builderState.CurrentProcedure];
								}
								else if (top.repeat > 1)
								{
									builderState.CurrentLine = 0;
									builderState.Callstack.Push((top.fromProcedure, top.fromLine, top.toProcedure, top.repeat - 1));
								}
								break;
							}
						case ProgramBuilderCommand.Finish:
							{
								builderState.Completed = true;
								finish = true;
								break; // END
							}
						case ProgramBuilderCommand.Pause:
							{
								builderState.CurrentLine += 1;
								finish = true;
								break;
							}
					}
				}
				catch (Exception e)
				{
					var msg = String.Format("{0} : {1}", frame, e.ToString());
					Logger.Instance.Error(this, "compile error", msg);
					return (null, null, new Dictionary<IAction, (int, int)>(), e.Message);
				}

				var currentPos = state.AxisState.Params.CurrentCoordinateSystem.ToLocal(state.AxisState.TargetPosition);
				state.VarsState.Vars["x"] = currentPos.x;
				state.VarsState.Vars["y"] = currentPos.y;
				state.VarsState.Vars["z"] = currentPos.z;
			}

			state = builder.FinishProgram(program, state);
			return (program, builderState, actionLines, "");
		}
	}
}
