using System;
using System.Collections.Generic;
using System.Linq;
using Log;
using Config;
using ProgramBuilder;
using RTSender;
using ModbusSender;
using Machine;
using Actions;

using CNCState;
using Actions.Mills;
using GCodeMachine;

namespace ProgramBuilder.Excellon
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

		

		private CNCState.CNCState Process(Arguments frame,
										  ActionProgram.ActionProgram program,
										  CNCState.CNCState state)
		{
			if (frame.SingleOptions.ContainsKey('X') && frame.SingleOptions.ContainsKey('Y'))
			{
				return ProcessDrillingMove(frame, program, state);
			}
			else if (frame.SingleOptions.ContainsKey('T'))
			{
				if (!frame.SingleOptions.ContainsKey('C'))
				{
					var tool = frame.SingleOptions['T'].ivalue1;
					state = builder.ProcessToolChange(tool, program, state);
					program.AddBreak(state);
				}
			}
			return state;
		}

		public (ActionProgram.ActionProgram actionProgram, IReadOnlyDictionary<IAction, int> actionLines, string errorMessage) BuildProgram(CNCState.CNCState initialMachineState, Sequence source)
		{
			var program = new ActionProgram.ActionProgram(rtSender, modbusSender, config, machine, toolManager);
			var actionLines = new Dictionary<IAction, int>();
			
			var state = builder.BeginProgram(program, initialMachineState);
			actionLines[program.Actions[0].action] = -1;
			
			state = builder.ProcessDrillingMove(null, null, -1, 10, 10, program, state);

			for (int line = 0; line < source.Lines.Count; line++)
			{
				Logger.Instance.Debug(this, "build", string.Format("processing line {0}", line));
				
				Arguments frame = source.Lines[line];
				try
				{
					var len0 = program.Actions.Count;
					state = Process(frame, program, state);
					var len1 = program.Actions.Count;
					if (len1 > len0)
					{
						var first = program.Actions[len0].action;
						actionLines[first] = line;
					}
				}
				catch (Exception e)
				{
					var msg = String.Format("{0} : {1}", frame, e.ToString());
					Logger.Instance.Error(this, "compile error", msg);
					return (null, new Dictionary<IAction, int>(), e.Message);
				}

				var currentPos = state.AxisState.Params.CurrentCoordinateSystem.ToLocal(state.AxisState.TargetPosition);
				state.VarsState.Vars["x"] = currentPos.x;
				state.VarsState.Vars["y"] = currentPos.y;
				state.VarsState.Vars["z"] = currentPos.z;
			}

			state = builder.FinishProgram(program, state);
			return (program, actionLines, "");
		}
	}
}
