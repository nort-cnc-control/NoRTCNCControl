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

			state.DrillingState.RetractDepth = DrillingState.RetractDepthType.RHeight;
			return builder.ProcessDrillingMove(X, Y, Z, R, Q, program, state);
		}

		private (CNCState.CNCState, bool) Process(Arguments frame, ActionProgram.ActionProgram program, CNCState.CNCState state)
		{
			if (frame.SingleOptions.ContainsKey('X') && frame.SingleOptions.ContainsKey('Y'))
			{
				return (ProcessDrillingMove(frame, program, state), false);
			}
			else if (frame.SingleOptions.ContainsKey('T'))
			{
				if (!frame.SingleOptions.ContainsKey('C'))
				{
					var tool = frame.SingleOptions['T'].ivalue1;
					state = builder.ProcessToolChange(tool, program, state);
					program.AddBreak(state);
					return (state, true);
				}
			}
			return (state, false);
		}

		public (ActionProgram.ActionProgram actionProgram, ProgramBuildingState finalState, IReadOnlyDictionary<IAction, (int procedure, int line)> actionLines, string errorMessage) BuildProgram(CNCState.CNCState initialMachineState, ProgramBuildingState builderState)
		{
			var program = new ActionProgram.ActionProgram(rtSender, modbusSender, config, machine, toolManager);
			var actionLines = new Dictionary<IAction, (int, int)>();
			
			var state = builder.BeginProgram(program, initialMachineState);
			actionLines[program.Actions[0].action] = (-1, -1);
			
			Sequence sequence = builderState.Source.Procedures[builderState.CurrentProcedure];

			state = builder.ProcessDrillingMove(null, null, -1, 10, 10, program, state);

			bool finish = false;
			builderState.Completed = false;

			while (!finish)
			{
				bool stop;
				if (builderState.CurrentLine >= sequence.Lines.Count)
					break;

				Logger.Instance.Debug(this, "build", string.Format("processing line {0}", builderState.CurrentLine));
				
				Arguments frame = sequence.Lines[builderState.CurrentLine];
				try
				{
					var len0 = program.Actions.Count;
					(state, stop) = Process(frame, program, state);

					var len1 = program.Actions.Count;
					if (len1 > len0)
					{
						Logger.Instance.Debug(this, "build", "has actions");
						var first = program.Actions[len0].action;
						actionLines[first] = (builderState.CurrentProcedure, builderState.CurrentLine);
					}
					else
					{	
						Logger.Instance.Debug(this, "build", "Doesn't have actions");
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

				builderState.CurrentLine += 1;
				if (stop)
					break;
			}

			if (builderState.CurrentLine >= sequence.Lines.Count)
				builderState.Completed = true;

			state = builder.FinishProgram(program, state);
			return (program, builderState, actionLines, "");
		}
	}
}

