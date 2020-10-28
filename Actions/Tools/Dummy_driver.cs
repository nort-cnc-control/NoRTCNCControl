using System;
using CNCState;

namespace Actions.Tools
{
    public class Dummy_driver
    {
        public IAction CreateAction()
        {
            return new PlaceholderAction();
        }
    }
}
