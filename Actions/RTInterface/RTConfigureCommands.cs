using System;
namespace Actions
{
    public class RTConfigureSteppersCommand : IRTCommand
    {
        public RTConfigureSteppersCommand(decimal spx, decimal spy, decimal spz)
        {
            Command = $"M100 X{spx:0.000} Y{spx:0.000} Z{spx:0.000}";
        }

        public bool CommandIsCached => false;

        public string Command { get; private set; }
    }

    public class RTConfigureFeedCommand : IRTCommand
    {
        public RTConfigureFeedCommand(decimal feed_max, decimal acc_def, decimal feed_base)
        {
            Command = $"M100 F{feed_max:0.000} A{acc_def:0.000} B{feed_base:0.000}";
        }

        public bool CommandIsCached => false;

        public string Command { get; private set; }
    }
}
