using System;
namespace Actions.Mills
{
    public interface IMillManager
    {
        bool ToolChangeInterrupts { get; }
        void SelectMill(int millId);
    }
}
