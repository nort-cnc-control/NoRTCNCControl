using System;
using System.Threading;
using Machine;
using Actions;
using RTSender;
using System.Globalization;
using Log;
using Vector;
using Config;

namespace ReadStatusMachine
{
    public class ReadStatusMachine : IMachine, ILoggerSource
    {
        private bool run;
        private readonly IRTSender rtSender;
        private Thread askPosThread;

        private readonly int timeoutT;
        private readonly int updateT;
        private readonly int maxretry;

        private EventWaitHandle updateWait;
        private MachineParameters config;

        public event Action<Vector3, bool, bool, bool, bool> CurrentStatusUpdate;

        public ReadStatusMachine(MachineParameters config, IRTSender rtSender, int updateT, int timeoutT, int maxretry)
        {
            this.maxretry = maxretry;
            this.updateT = updateT;
            this.timeoutT = timeoutT;

            this.config = config;
            this.rtSender = rtSender;

            updateWait = new EventWaitHandle(false, EventResetMode.AutoReset);
        }

        public State RunState { get; private set; }

        public string Name => "read status machine";

        public void Abort()
        {
            run = false;
            updateWait.Set();
            askPosThread.Join();
        }

        public void Activate()
        {
        }

        public void Continue()
        {
            Logger.Instance.Debug(this, "action", "continue");
            run = true;
            askPosThread = new Thread(new ThreadStart(AskHardwareState));
            askPosThread.Start();
        }

        public void Dispose()
        {
            run = false;
            updateWait.Set();
            askPosThread.Join();
        }

        public void Pause()
        {
            Logger.Instance.Debug(this, "action", "pause");
            run = false;
            updateWait.Set();
            askPosThread.Join();
        }

        public void Reboot()
        {
        }

        public Vector3 ReadHardwareCoordinates()
        {
            Logger.Instance.Debug(this, "readhw", "wait for lock");
            int retry = 0;
            Logger.Instance.Debug(this, "readhw", "read coordinates");
            while (true)
            {
                RTAction action = new RTAction(rtSender, new RTGetPositionCommand());
                // action.ReadyToRun.WaitOne();
                action.Run();
                action.Finished.WaitOne(timeoutT);

                if (action.ActionResult == null ||
                    !action.ActionResult.ContainsKey("X") ||
                    !action.ActionResult.ContainsKey("Y") ||
                    !action.ActionResult.ContainsKey("Z"))
                {
                    action.Dispose();
                    Logger.Instance.Warning(this, "readhw", String.Format("Can not read coordinates, retry."));
                    retry++;
                    if (retry >= maxretry)
                    {
                        Logger.Instance.Error(this, "readhw", String.Format("Max retry exceed"));
                        throw new TimeoutException();
                    }
                    Thread.Sleep(300);
                    continue;
                }
                var xs = action.ActionResult["X"];
                var ys = action.ActionResult["Y"];
                var zs = action.ActionResult["Z"];
                action.Dispose();

                var pos = new Vector3(decimal.Parse(xs, CultureInfo.InvariantCulture),
                                      decimal.Parse(ys, CultureInfo.InvariantCulture),
                                      decimal.Parse(zs, CultureInfo.InvariantCulture));
                pos.x /= config.X_axis.steps_per_mm;
                pos.y /= config.Y_axis.steps_per_mm;
                pos.z /= config.Z_axis.steps_per_mm;
                return pos;
            }
        }

        public (bool ex, bool ey, bool ez, bool ep) ReadCurrentEndstops()
        {
            Logger.Instance.Debug(this, "readhw", "read endstops");
            int retry = 0;
            while (true)
            {
                try
                {
                    RTAction action = new RTAction(rtSender, new RTGetEndstopsCommand());
                    // action.ReadyToRun.WaitOne();
                    action.Run();
                    action.Finished.WaitOne(timeoutT);
                    return (action.ActionResult["EX"] == "1",
                            action.ActionResult["EY"] == "1",
                            action.ActionResult["EZ"] == "1",
                            action.ActionResult["EP"] == "1");
                }
                catch (Exception e)
                {
                    Logger.Instance.Warning(this, "readhw", String.Format("Can not read position, retry. {0}", e));
                    retry++;
                    if (retry >= maxretry)
                    {
                        Logger.Instance.Warning(this, "readhw", String.Format("Max retry exceed"));
                        throw e;
                    }
                    Thread.Sleep(300);
                }
            }
        }

        private void AskHardwareState()
        {
            Logger.Instance.Debug(this, "run", "start reading thread");
            while (run)
            {
                try
                {
                    var hw_crds = ReadHardwareCoordinates();
                    var (ex, ey, ez, ep) = ReadCurrentEndstops();
                    CurrentStatusUpdate?.Invoke(hw_crds, ex, ey, ez, ep);
                }
                catch
                {
                    Logger.Instance.Error(this, "timeout", "Can not read state");
                }
                updateWait.WaitOne(updateT);
            }
            Logger.Instance.Debug(this, "run", "finish reading thread");
        }

        public void Start()
        {
            Logger.Instance.Debug(this, "action", "start");
        }

        public void Stop()
        {
            Logger.Instance.Debug(this, "action", "stop");
            run = false;
            askPosThread.Join();
        }
    }
}
