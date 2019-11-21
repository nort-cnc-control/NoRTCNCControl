using System;
using Actions;

namespace ActionExecutor
{
    public interface IActionExecutor
    {
        event Action<IAction> ActionStarted;
    }
}
