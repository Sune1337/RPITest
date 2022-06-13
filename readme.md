[Enabling hardware and software timestamps (Windows-specific)](https://license.fsmlabs.com/docs/features/hwstamps.html)

```
PS C:\> Set-ExecutionPolicy remotesigned # Enable execution of below steps in some environments
PS C:\> Install-Module SoftwareTimeStamping
PS C:\> Import-Module SoftwareTimestamping
PS C:\> Get-DscResource -Module SoftwareTimeStamping
PS C:\> Enable-SWTimestamping -NetAdapterName Eth4
PS C:\> Restart-NetAdapter -Name Eth4
```