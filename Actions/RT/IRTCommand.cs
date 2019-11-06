using System;

namespace Actions
{
    public interface IRTCommand
    {
        bool CommandIsCached { get; }
        String Command { get; }
    }
}
