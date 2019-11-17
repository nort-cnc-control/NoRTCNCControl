namespace Actions
{
    public class RTGetPositionCommand : IRTCommand
    {
        public bool CommandIsCached => false;

        public string Command => "M114";
    }

    public class RTGetEndstopsCommand : IRTCommand
    {
        public bool CommandIsCached => false;

        public string Command => "M119";
    }
}
