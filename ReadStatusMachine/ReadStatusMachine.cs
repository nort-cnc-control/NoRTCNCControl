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
        private readonly int timeout;
        private EventWaitHandle timeoutWait;
        private MachineParameters config;

        public event Action<Vector3, bool, bool, bool, bool> CurrentStatusUpdate;

        public ReadStatusMachine(MachineParameters config, IRTSender rtSender, int updateT)
        {
            this.config = config;
            this.rtSender = rtSender;
            timeout = updateT;
            timeoutWait = new EventWaitHandle(false, EventResetMode.AutoReset);
        }

        public State RunState { get; private set; }

        public string Name => "read status machine";

        public void Abort()
        {
            run = false;
            timeoutWait.Set();
            askPosThread.Join();
        }

        public void Activate()
        {
        }

        public void Continue()
        {
            Logger.Instance.Debug(this, "action", "continue");
            run = true;
            askPosThread = new Thread(new ThreadStart(AskPosition));
            askPosThread.Start();
        }

        public void Dispose()
        {
            run = false;
            timeoutWait.Set();
            askPosThread.Join();
        }

        public void Pause()
        {
            Logger.Instance.Debug(this, "action", "pause");
            run = false;
            timeoutWait.Set();
            askPosThread.Join();
        }

        public void Reboot()
        {
        }

        public Vector3 ReadHardwareCoordinates()
        {
            Logger.Instance.Debug(this, "readhw", "read coordinates");
            while (true)
            {
                try
                {
                    RTAction action = new RTAction(rtSender, new RTGetPositionCommand());
                    // action.ReadyToRun.WaitOne();
                    action.Run();
                    action.Finished.WaitOne(500);
                    var xs = action.ActionResult["X"];
                    var ys = action.ActionResult["Y"];
                    var zs = action.ActionResult["Z"];
                    var pos = new Vector3(decimal.Parse(xs, CultureInfo.InvariantCulture),
                                          decimal.Parse(ys, CultureInfo.InvariantCulture),
                                          decimal.Parse(zs, CultureInfo.InvariantCulture));
                    pos.x /= config.steps_per_x;
                    pos.y /= config.steps_per_y;
                    pos.z /= config.steps_per_z;
                    return pos;
                }
                catch (Exception e)
                {
                    Logger.Instance.Warning(this, "readhw", String.Format("Can not read coordinates, retry. {0}", e));
                    Thread.Sleep(300);
                }
            }
        }

        public (bool ex, bool ey, bool ez, bool ep) ReadCurrentEndstops()
        {
            RTAction action = new RTAction(rtSender, new RTGetEndstopsCommand());
            // action.ReadyToRun.WaitOne();
            action.Run();
            action.Finished.WaitOne();
            return (action.ActionResult["EX"] == "1",
                    action.ActionResult["EY"] == "1",
                    action.ActionResult["EZ"] == "1",
                    action.ActionResult["EP"] == "1");
        }

        private void AskPosition()
        {
            Logger.Instance.Debug(this, "run", "start reading thread");
            while (run)
            {
                try
                {
                    var hw_crds = ReadHardwareCoordinates();
                    //var (ex, ey, ez, ep) = ReadCurrentEndstops();
                    var (ex, ey, ez, ep) = (false, false, false, false);
                    CurrentStatusUpdate?.Invoke(hw_crds, ex, ey, ez, ep);
                }
                catch
                {
                    ;
                }
                timeoutWait.WaitOne(timeout);
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
