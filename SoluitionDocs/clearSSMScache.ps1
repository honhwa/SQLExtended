 # Clear SSMS 22 MEF component cache
  Remove-Item "$env:LOCALAPPDATA\Microsoft\SSMS\22.0_6ee4710c\ComponentModelCache\*" -Recurse -Force -ErrorAction SilentlyContinue

  # Clear SSMS 22 extension cache
  Remove-Item "$env:LOCALAPPDATA\Microsoft\SSMS\22.0_6ee4710c\Extensions\*" -Recurse -Force -ErrorAction SilentlyContinue

  
