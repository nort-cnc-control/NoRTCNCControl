﻿using System;
using System.Collections.Generic;
using Machine;

namespace CNCState
{
    public class VarsState : IState
    {
        public Dictionary<string, decimal> Vars;
        public VarsState()
        {
            Vars = new Dictionary<string, decimal>();
        }

        VarsState BuildCopy()
        {
            var st = new VarsState();
            foreach (var item in Vars)
                st.Vars[item.Key] = item.Value;
            return st;
        }
    }
}
