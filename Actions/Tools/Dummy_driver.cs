using System;
using CNCState;

namespace Actions.Tools
{
    public class Dummy_driver : IDriver
    {
        public IAction Configure()
        {
            return new PlaceholderAction();
        }

        public IAction CreateAction()
        {
            return new PlaceholderAction();
        }
    }
}
