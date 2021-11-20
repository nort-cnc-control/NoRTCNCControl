using System.Collections.Generic;
using Log;
using CNCState;
using ProgramBuilder;
using Machine;
using Actions;
using Actions.Mills;
using Config;
using System.Linq;
using System;
using RTSender;
using ModbusSender;
using GCodeMachine;

namespace ProgramBuilder.GCode
{
	public class ProgramBuilder_GCode : ILoggerSource
	{
		private readonly MachineParameters config;

		private readonly ProgramBuilder builder;

		public string Name => "gcode builder";

		private readonly IRTSender rtSender;
		private readonly IModbusSender modbusSender;
		private readonly IMachine machine;
		private readonly IMillManager toolManager;

		public ProgramBuilder_GCode(ProgramBuilder builder,
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

		private CNCState.CNCState ProcessParameters(Arguments block,
									   ActionProgram.ActionProgram program,
									   CNCState.CNCState state)
		{
			state = state.BuildCopy();
			if (block.Feed != null)
			{
				state.AxisState.Feed = ProgramBuilder.ConvertSizes(GetValue(block.Feed, state).Value, state) / 60.0m; // convert from mm/min to mm/sec
				state.AxisState.Feed = Math.Max(state.AxisState.Feed, config.basefeed);
			}
			return state;
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

		private CNCState.CNCState ProcessDirectMove(Arguments block,
													ActionProgram.ActionProgram program,
													CNCState.CNCState state)
		{
			decimal? X = GetValue(block.X, state);
			decimal? Y = GetValue(block.Y, state);
			decimal? Z = GetValue(block.Z, state);
			decimal? I = GetValue(block.I, state);
			decimal? J = GetValue(block.J, state);
			decimal? K = GetValue(block.K, state);
			decimal? R = GetValue(block.R, state);

			CNCState.AxisState.MType moveType;

			var cmd = block.Options.FirstOrDefault((arg) => arg.letter == 'G');
            if (cmd != null)
            {
                switch (cmd.ivalue1)
                {
                    case 0:
                        moveType = AxisState.MType.FastLine;
                        break;
                    case 1:
                        moveType = AxisState.MType.Line;
                        break;
                    case 2:
                        moveType = AxisState.MType.ArcCW;
                        break;
                    case 3:
                        moveType = AxisState.MType.ArcCCW;
                        break;
					default:
						return state;
                }
            }
			else
			{
				moveType = state.AxisState.MoveType;
			}

			state.AxisState.MoveType = moveType;
			switch (moveType)
			{
				case AxisState.MType.FastLine:
					return builder.ProcessLineMove(X, Y, Z, true, program, state);
				case AxisState.MType.Line:
					return builder.ProcessLineMove(X, Y, Z, false, program, state);
				case AxisState.MType.ArcCW:
					return builder.ProcessArcMove(X, Y, Z, I, J, K, R, false, program, state);
				case AxisState.MType.ArcCCW:
					return builder.ProcessArcMove(X, Y, Z, I, J, K, R, true, program, state);
			}
			return state;
		}

		private CNCState.CNCState ProcessMove(Arguments block,
											  ActionProgram.ActionProgram program,
											  CNCState.CNCState state)
		{
			if (!state.DrillingState.Drilling)
				return ProcessDirectMove(block, program, state);
			else
				return ProcessDrillingMove(block, program, state);
		}

		private CNCState.CNCState ProcessToolCommand(Arguments block,
													 ActionProgram.ActionProgram program,
													 CNCState.CNCState state)
		{
			var cmd = block.Options.FirstOrDefault((arg) => (arg.letter == 'M'));
			if (cmd == null)
				return state;

			Dictionary<string, decimal> options = new Dictionary<string, decimal>();

			// Rotation direction
			int command = cmd.ivalue1;
			if (command == 3)
				options["ccw"] = 0;
			else if (command == 4)
				options["ccw"] = 1;

			// Tool ID
			int tool_id;
			if (cmd.dot)
                tool_id = cmd.ivalue2;
            else
                tool_id = config.deftool_id;

			// Rotation speed
			var S = block.Options.FirstOrDefault((arg) => arg.letter == 'S');
            if (S != null)
				options["speed"] = S.value;

			switch (cmd.ivalue1)
			{
				case 3:
				case 4:
					return builder.ProcessToolStart(tool_id, options, program, state);
				case 5:
					return builder.ProcessToolStop(tool_id, program, state);
			}
			return state;
		}

		private CNCState.CNCState ProcessSyncToolCommand(Arguments block,
														 ActionProgram.ActionProgram program,
														 CNCState.CNCState state)
		{
			var cmd = block.Options.FirstOrDefault((arg) => (arg.letter == 'M'));
			if (cmd == null)
				return state;

			int tool;
			if (block.SingleOptions.ContainsKey('T'))
            {
                tool = block.SingleOptions['T'].ivalue1;
            }
            else
            {
                tool = state.SyncToolState.Tool;
            }

			switch (cmd.ivalue1)
			{
				case 703:
					return builder.ProcessSyncToolStart(tool, program, state);
				case 705:
					return builder.ProcessSyncToolStop(tool, program, state);
			}
			return state;
		}

		private CNCState.CNCState ProcessCoordinatesSet(Arguments args,
														ActionProgram.ActionProgram program,
														CNCState.CNCState state)
		{
			decimal? X = GetValue(args.X, state);
			decimal? Y = GetValue(args.Y, state);
			decimal? Z = GetValue(args.Z, state);
			return builder.CoordinatesSet(X, Y, Z, program, state);
		}

		private CNCState.CNCState ProcessCoordinatesSystemSet(Arguments block,
												 ActionProgram.ActionProgram program,
												 CNCState.CNCState state)
		{
			var cmd = block.Options.FirstOrDefault((arg) => (arg.letter == 'G'));
			if (cmd == null)
				return state;
			if (cmd.ivalue1 < 53 || cmd.ivalue1 > 59)
				return state;

			return builder.CoordinatesSystemSet(cmd.ivalue1 - 53, program, state);
		}

		private IReadOnlyList<Arguments> SplitFrame(Arguments args)
		{
			List<Arguments> arguments = new List<Arguments>();

			Arguments cur = new Arguments();

			foreach (var arg in args.Options)
			{
				if (arg.letter == 'M' || arg.letter == 'G')
				{
					if (cur.Options.Count > 0)
					{
						arguments.Add(cur);
						cur = new Arguments();
					}
				}
				cur.AddOption(arg);
			}

			if (cur.Options.Count > 0)
			{
				arguments.Add(cur);
				cur = new Arguments();
			}

			return arguments;
		}

		
		private (int, int) CallSubprogram(Arguments args,
										  CNCState.CNCState state)
		{
			if (!args.SingleOptions.ContainsKey('P'))
				throw new InvalidOperationException("Subprogram id not specified");

			int prgid = args.SingleOptions['P'].ivalue1;
			int amount = 1;
			if (args.SingleOptions.ContainsKey('L'))
				amount = args.SingleOptions['L'].ivalue1;

			return (prgid, amount);
		}

		private enum ProgramBuilderCommand
        {
            Continue,
            Call,
            Return,
            Pause,
            Finish,
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
			ProgramBuilderCommand command = ProgramBuilderCommand.Continue;
			int pid = -1, amount = -1;
			state = state.BuildCopy();
			state = ProcessParameters(block, program, state);

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
				state = ProcessMove(block, program, state);
			}
			else if (cmd.letter == 'G')
			{
				switch (cmd.ivalue1)
				{
					case 0:
					case 1:
					case 2:
					case 3:
						state = ProcessMove(block, program, state);
						break;
					case 4:
						try
                        {
							if (block.SingleOptions.ContainsKey('P'))
							{
                        		var P = block.SingleOptions['P'];
                        		var dt = GetValue(P, state).Value;
								state = builder.AddPause(dt, program, state);
							}
                        }
						catch
						{
							;
						}
						break;
					case 17:
						state.AxisState.Params.CurrentPlane = AxisState.Plane.XY;
						break;
					case 18:
						state.AxisState.Params.CurrentPlane = AxisState.Plane.YZ;
						break;
					case 19:
						state.AxisState.Params.CurrentPlane = AxisState.Plane.ZX;
						break;
					case 20:
						state.AxisState.Params.SizeUnits = AxisState.Units.Inches;
						break;
					case 21:
						state.AxisState.Params.SizeUnits = AxisState.Units.Millimeters;
						break;
					case 28:
						state = builder.AddHoming(program, state);
						break;
					case 30:
						state = builder.AddZProbe(program, state);
						break;
					case 53:
					case 54:
					case 55:
					case 56:
					case 57:
					case 58:
					case 59:
						state = ProcessCoordinatesSystemSet(block, program, state);
						break;
					case 80:
						state.DrillingState.Drilling = false;
						state = ProcessMove(block, program, state);
						break;
					case 81:
						state.DrillingState.Drilling = true;
						state.DrillingState.Peck = false;
						state.DrillingState.Retract = DrillingState.RetractType.Rapid;
						state.DrillingState.RetractReverse = false;
						state.DrillingState.Dwell = false;
						state.DrillingState.LeftHand = false;
						state.DrillingState.StopSpindle = false;
						state.DrillingState.Tapping = false;
						state = ProcessMove(block, program, state);
						break;
					case 82:
						state.DrillingState.Drilling = true;
						state.DrillingState.Peck = false;
						state.DrillingState.Retract = DrillingState.RetractType.Rapid;
						state.DrillingState.RetractReverse = false;
						state.DrillingState.Dwell = true;
						state.DrillingState.LeftHand = false;
						state.DrillingState.StopSpindle = false;
						state.DrillingState.Tapping = false;
						state = ProcessMove(block, program, state);
						break;
					case 83:
						state.DrillingState.Drilling = true;
						state.DrillingState.Peck = true;
						state.DrillingState.Retract = DrillingState.RetractType.Rapid;
						state.DrillingState.RetractReverse = false;
						state.DrillingState.Dwell = false;
						state.DrillingState.LeftHand = false;
						state.DrillingState.StopSpindle = false;
						state.DrillingState.Tapping = false;
						state = ProcessMove(block, program, state);
						break;
					case 84:
						state.DrillingState.Drilling = true;
						state.DrillingState.Peck = false;
						state.DrillingState.Retract = DrillingState.RetractType.Feed;
						state.DrillingState.RetractReverse = true;
						state.DrillingState.Dwell = false;
						state.DrillingState.LeftHand = false;
						state.DrillingState.StopSpindle = false;
						state.DrillingState.Tapping = true;
						state = ProcessMove(block, program, state);
						break;
					case 85:
						state.DrillingState.Drilling = true;
						state.DrillingState.Peck = false;
						state.DrillingState.Retract = DrillingState.RetractType.Feed;
						state.DrillingState.RetractReverse = false;
						state.DrillingState.Dwell = false;
						state.DrillingState.LeftHand = false;
						state.DrillingState.StopSpindle = false;
						state.DrillingState.Tapping = false;
						state = ProcessMove(block, program, state);
						break;
					case 86:
						state.DrillingState.Drilling = true;
						state.DrillingState.Peck = false;
						state.DrillingState.Retract = DrillingState.RetractType.Feed;
						state.DrillingState.RetractReverse = false;
						state.DrillingState.Dwell = false;
						state.DrillingState.LeftHand = false;
						state.DrillingState.StopSpindle = true;
						state.DrillingState.Tapping = false;
						state = ProcessMove(block, program, state);
						break;
					case 87:
						state.DrillingState.Drilling = true;
						state.DrillingState.Peck = false;
						state.DrillingState.Retract = DrillingState.RetractType.Manual;
						state.DrillingState.RetractReverse = false;
						state.DrillingState.Dwell = false;
						state.DrillingState.LeftHand = false;
						state.DrillingState.StopSpindle = true;
						state.DrillingState.Tapping = false;
						state = ProcessMove(block, program, state);
						break;
					case 88:
						state.DrillingState.Drilling = true;
						state.DrillingState.Peck = false;
						state.DrillingState.Retract = DrillingState.RetractType.Manual;
						state.DrillingState.RetractReverse = false;
						state.DrillingState.Dwell = true;
						state.DrillingState.LeftHand = false;
						state.DrillingState.StopSpindle = true;
						state.DrillingState.Tapping = false;
						state = ProcessMove(block, program, state);
						break;
					case 89:
						state.DrillingState.Drilling = true;
						state.DrillingState.Peck = false;
						state.DrillingState.Retract = DrillingState.RetractType.Feed;
						state.DrillingState.RetractReverse = false;
						state.DrillingState.Dwell = true;
						state.DrillingState.LeftHand = false;
						state.DrillingState.StopSpindle = false;
						state.DrillingState.Tapping = false;
						state = ProcessMove(block, program, state);
						break;

					case 90:
						state.AxisState.Absolute = true;
						break;
					case 91:
						state.AxisState.Absolute = false;
						break;
					case 92:
						state = ProcessCoordinatesSet(block, program, state);
						break;
					case 98:
						state.DrillingState.RetractDepth = DrillingState.RetractDepthType.InitialHeight;
						break;
					case 99:
						state.DrillingState.RetractDepth = DrillingState.RetractDepthType.RHeight;
						break;
				}
			}
			else if (cmd.letter == 'M')
			{
				switch (cmd.ivalue1)
				{
					case 0:
						program.AddBreak(state);
						command = ProgramBuilderCommand.Pause;
						break;
					case 2:
						program.AddStop(state);
						command = ProgramBuilderCommand.Finish;
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
							state = builder.ProcessToolChange(tool, program, state);
							if (toolManager.ToolChangeInterrupts)
								command = ProgramBuilderCommand.Pause;
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

		private (CNCState.CNCState, ProgramBuilderCommand command, int programid, int amount) Process(Arguments args,
																										ActionProgram.ActionProgram program,
																										CNCState.CNCState state,
																										int curprogram,
																										int curline,
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
