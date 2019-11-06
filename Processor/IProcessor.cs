using System;
using ActionProgram;

namespace Processor
{
    public interface IProcessor
    {
        void ProcessProgram(ActionProgram.ActionProgram program);
    }
}
