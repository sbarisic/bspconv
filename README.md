# bspconv
Quake bsp map converter tool
The release is a pure .NET Framework 4.6 executable file, so it should work on other operating systems other than windows if you have a .NET runtime

![image](https://github.com/sbarisic/bspconv/assets/3836060/33afac9d-9318-4145-b8ff-1ed2d8343d92)

Currently only Quake 3 (IBSP 46) and Quake Live (IBSP 47) maps are supported.
Feature requests welcome.
If you encounter any problems, please open a new issue here.


## Quake 3 to Quake Live example

https://lvlworld.com/download/id:2527

![image](https://github.com/sbarisic/bspconv/assets/3836060/7ddaa0a0-5879-4506-a8b1-5db1ba9dbb3b)


## Quake Live to Quake 3 example

bloodrun.bsp

``bspconv.exe 46 pak00.pk3 --bsp:bloodrun.bsp``

![image](https://github.com/sbarisic/bspconv/assets/3836060/9cc7ea6d-ec39-4220-8f54-a25b47400a42)
