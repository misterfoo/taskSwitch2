********************************************************
Important notes about debugging and installing this app:
********************************************************

----------------------------------------------------------
Specifying uiAccess in the manifest

In order to call SwitchToThisWindow/SetForegroundWindow, this application requires a manifest which 
specifies uiAccess=true in the requestedExecutionLevel element. You set this by editing app.manifest.
However, for debugging you will need to set this to false, otherwise the debugger will require elevation
to actually launch.

The best way to debug the UI aspects of the program is to edit the manifest as described above and
then run the app with the /uitest flag. This opens the window and leaves it open while you mess around
with it.

----------------------------------------------------------
Installing for actual use

Applications which specify true for uiAccess must be signed and must be installed in the regular
Program Files area. To sign this application, you must use signtool.exe from the Windows SDK.

This page has good information:
  http://www.codeproject.com/Articles/325833/Basics-of-Signing-and-Verifying-code

There should be a self-signed certificate (taskSwitcher.pfx) in the same directory as this project,
which can be used to sign the final executable as follows:

  signtool.exe sign /f taskSwitcher.pfx taskSwitcher2.exe

This certificate was created using the steps outlined here:
  http://msdn.microsoft.com/en-us/library/ff699202.aspx

Specifically I did this:

  makecert.exe -sv taskSwitcher.pvk -n "cn=Task Switcher Certificate" taskSwitcher.cer -b 11/01/2013 -e 01/01/2020 -r
  pvk2pfx.exe -pvk taskSwitcher.pvk -spc taskSwitcher.cer -pfx taskSwitcher.pfx

Then you need to install the certificate in the Trusted Root Certification Authorities area on your machine,
which you can do by double-clicking the .pfx file and installing it in the "Trusted Root Certification
Authorities" store.

If you try to run the switcher and get an error saying "A referral was returned from the server.", it
means one of the avove steps was not done correctly. You can check that the exe is properly signed by 
right-clicking and choosing Properties; there should be a "Digital Signatures" tab showing the TaskSwitcher
certificate. You can browse the installed certificates by running certmgr.msc.
