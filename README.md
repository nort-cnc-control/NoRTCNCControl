# Description

This is a control system for cnc milling machines. It works in conjuction with https://github.com/nort-cnc-control/cnccontrol_rt,
which performs realtime operations, such as steppers control and end-stops detection.

No realtime kernel is required!

# Supported third-party hardware

- Hyundai N700E vector inverter for spindel

# Components

## Running command server

`mono NoRTServer.exe`

### Supported options
- -l debug.log - debug log to file
- -r runConfig.json - execution configuration, such as communication with hardware
- -m machineConfiguration - physical hardware configuration. Maximal feed, acceleration, table sizes, axis orientation, etc
- -p - control connection port. default=8888
- -h - print help

### Emulation mode

In this mode no hardware is required, commands to hardware just printed in terminal


Use this run config for full emulation:
```
{
    "modbus_sender" : {
        "sender" : "EmulationModbusSender"
    },

    "rt_sender" : {
        "sender" : "EmulationRTSender"
    },

    "spindle_driver" : "None"
}
```

# Supported operations

Cnccontrol supports G-Code commands described in ISO 6983-1:2009,
but only part of commands are implemented.

## Units

All sizes should be specified in millimeters (mm).

## List of supported operations

### G commands

- G00 - fast movement. Moves by line. Args: X Y Z - coordinates of movement
- G01 - linear movement with specified. . Args: X Y Z - coordinates of movement
- G02 - clockwise arc movement. Only flat move is now supported. Args: X Y Z - coordinates of movement, R - radius of moving, I J K - relative coordinates of arc center
- G03 - counterclockwise arc movement. Only flat move is now supported. Args: X Y Z - coordinates of movement, R - radius of moving, I J K - relative coordinates of arc center
- G09 - finish current movement with feedrate = 0
- G17 - select XY plane for arc movement
- G18 - select XZ plane for arc movement
- G19 - select YZ plane for arc movement
- G28 - search Z, X, Y endstops
- G30 - probe Z axis
- G53-G59 - select one of the coordinate systems
- G80 - finish drilling cycle
- G81 - drill without pecking, dwelling, tapping, retract with fast feed. Args: R - retract depth. Z - drilling depth, X Y - coordinates of drilling
- G90 - select absolute positioning
- G91 - select relative positioning
- G92 - set current position
- G98 - retract to initial height while drilling
- G99 - retract to R height while drilling

### M commands

- M00 - pause until 'Continue' pressed
- M02 - program end
- M03 - start spindel clockwise. Args: S - rpm
- M04 - start spindel counterclockwise. Args: S - rpm
- M05 - stop spindel
- M06 - change tool
- M120 - push state
- M121 - pop state
- M703 - start syncronous tool
- M705 - stop syncronous tool

### Options

- Sxxx - set spindel rotation speed, rpm
- Txxx - display 'Insert tool' message and wait for continue.
- Fxxx - set feetrate mm/min

### Coordinates

- X, Y, Z - coordinates of target position
- I, J, K - coordinates of arc center when G02/G03 specified
- R - For G2/G3: radius of arc, when G02/G03 specified. R < 0 means make big arc, with angle > 180. For G81: retract depth

## Reseting

CNC milling machine can be stopped in any moment with 'Reset' button in UI. It stops program execution and reboots board.
Note, that coordinates of spindel became invalid after reset, because immediate stop of mill, when it moves with big enougth feedrate can lead to slip. So we can not be sure about real spindel position.

## Coordinate systems

cnccontrol supports 7 coordinate systems, which are offseted related to coordinate table.

After searching endstops cutter position in harsware coordinate system sets to 0, 0, 0. After Z probe, cutter Z position in main coordinate system sets to 0. All offsets of G53-G59 systems are preserved.

# Movement optimizations

NoRTCNCControl optimizes movements. If we have N movements with same feedrate and direction, cutter won't stop between this movements except G09 is specified. When directions of 2 sequencial movements differs, feedrate is selected so that tangential velocity leap doesn't exceed allowed value.

# Dependencies

mono, nuget

# License

GNU GPLv3, full text of GNU GPLv3 see in LICENSE file
