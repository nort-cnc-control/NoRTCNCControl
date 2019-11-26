using System;
using System.Collections.Generic;

namespace Machine
{
    public interface IMessageRouter
    {
        void Message(IReadOnlyDictionary<string, string> message);
    }
}
