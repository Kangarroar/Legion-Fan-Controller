# Lenovo Legion 5/6 Fan Controller
A Windows utility for custom fan control on Lenovo Legion 5/6 series laptops. Based and forked from [LegionGen5-6FanControl_WinRing_Smokeless_I-OPortsMethod](https://github.com/Z4ndyz/LegionGen5-6FanControl_WinRing_Smokeless_I-OPortsMethod).
![App Screenshot](https://media.discordapp.net/attachments/720567770898497549/1354558659384053880/Legion_Fan_Controller_26-03-2025_17_52_07.png?ex=67e5ba8e&is=67e4690e&hm=aec4aad0c2a529085995d1dc4cd8de8d21d136441a59b475cad263f86cfa8f55&=&format=webp&quality=lossless) 

## Installation

1. Download the latest release from [Releases page](https://github.com/Kangarroar/Legion-5-6-Fan-Controller/releases)
2. Extract the ZIP file
3. Right-click `LenovoFanController.exe` → **Run as administrator**

Use at your own risk.

##  Requirements

- Windows 10/11 (64-bit)
- Lenovo Legion 5/6 series laptop
- Administrator privileges
- .NET 6.0 Desktop Runtime

___


# Original README.md
# LegionGen5-6FanControl_WinRing_Smokeless_I-OPortsMethod
```
Method of controlling Fans on Lenovo Legions Gen 5/6

Make sure to run the app (.exe) as ADMIN otherwise IT WON'T WORK.
Make sure WinRing0x64.sys is not blocked from being used on your system otherwise the app
won't work.

Use the fan_config.txt files values near the .exe after building the sollution

This project was done in my free time wanting to move away from RwDrv.sys and using a driver
that is less prone to throw IRQL errors and BSODS.
Previously i used LFC (Legion Fan Control by Rodpad) before using a script to control
fans that was still using RwDrv.sys albeit in a more secure manner.
Huge thanks to SmokelessCPU for allowing me to share the files with you and of course by
exposing a way to make use of WinRing0x64.sys and I/O ports to write and read from the EC.
https://github.com/SmokelessCPUv2
Huge thanks to @akillisandalye on Discord for helping me with EC mapping.

Disclaimer : Since i don't own a Legion Gen 5 i might not be able to bugfix it nor i'm
interested right now, someone could pick up this code and do said fixes.
Tested on my Legion Gen 6 Legion 5 Pro and it works flawlessly.

Fan Curves are checked and adjust themselves if needed every 15 seconds, if you build the source
yourself you can change the timer in main to a lower value.

You can set fan curves for Fn+Q modes as follow
Quiet Mode (1), Balanced Mode (2), and Performance and Custom mode (3/255) will share the same fan curve.
I could have separated them but i kept them the same curve since Legion Laptops do the same anyway.

Modify the value of legion_gen in the fan_config.txt files to corespond to your legion generation
5 or 6.

Fan RPM values can be a minimum of 0. i suggest to not use more than 5000, 4400 is lenovo max default
in curves, but turbo is usually 5000 range, even if set higher fans will go
only as fast as they can spin.

The other values in the config files i provided corespond to my fancurves

        # Number of points : 9
        # Acceleration/Deceleration Values will be 2
        # CPU Temps Ramp Up        11 45 55 60 65 70 75 80 90
        # Cpu Temps Ramp Down      10 43 53 58 63 68 73 78 87
        # Gpu Temps Ramp Up        11 50 55 60 63 66 69 72 75
        # Gpu Temps Ramp Down      10 48 53 58 61 63 67 70 73
        # Heatsink Temps Ramp Up   11 50 55 65 70 75 80 85 90
        # Heatsink Temps Ramp Down 10 48 53 63 68 73 78 83 85
        # RPM Quiet                0 0 0    1800 2500 3200 3500 3800 4400       
        # RPM Balanced             0 0 2200 3200 3500 3800 4100 4400 4700
        # RPM Performance/Custom   0 0 2200 3600 3900 4200 4500 4800 5000

Disclaimer regarding fan config files

Do not use more than 9 points otherwise they'll be ignored and issues might be arised, I didn't
test that extensively. Using less points than 9 should be fine since i did some padding and checks
and it seemed ok but i didn't have the time nor energy to fully go throughly.

Second Disclaimer regarding fan config files
If the RPM values ar not in ascending order, or the ramp up and ramp down temps not abiding
to an ascending order too fan curves won't work properly. Please make sure to use the rpm
values in an ascending order and ramp up and ramp down values too. Also please make sure
to have each ramp up value paired with a ramp down value that is smaller than the one it's paired
with. Hopefully the config files i provided represent a clear example.

There are some binaries built already but it would be better if everyone compiled them themselves
because Windows might flag them.
You can find them at FanControl/FanControl/bin/Debug/net8.0

App was built with net8.0.
Files that should be present to have the App Running are

runtimes
fan_config_balanced.txt
fan_config_perfcust.txt
fan_config_quiet.txt
FanControl.deps.json
FanControl.dll
FanControl.exe
FanControl.pdb
FanControl.runtimeconfig.json
System.CodeDom.dll
System.Management.dll
WinRing0x64.dll
WinRing0x64.dll
WinRing0x64.dll
WinRing0x64.sys

#Added an archive with a debug version .exe, this one will still apply FanCurves but
it will also display them.
If you use less fan curve points but see 10(int) and 0xA fan points always don't worry
Because i use padding up to this value, your curve will still be the same, and remember
don't use more than 9 points.
# 

Feel free to fork this and modify it as long as you provide the LICENSE.md

### I don't plan on doing an GUI nor do i have the experience for it, at least not right now, maybe ever so don't wait for it. ###
