﻿using System;

namespace ModbusSender
{
    public interface IModbusSender
    {
        void WriteRegister(UInt16 devid, UInt16 index, UInt16 value);
    }
}
