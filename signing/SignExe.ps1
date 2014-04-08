
param( [string] $exePath )

function VerifyExists( $path )
{
	if( !(Test-Path $path) )
	{
		throw "Can't find $path"
	}
}

$scriptPath = Split-Path $MyInvocation.MyCommand.Definition

$pfx = Join-Path $scriptPath "taskSwitcher.pfx"
VerifyExists $pfx

$signTool = Join-Path $scriptPath "signTool.exe"
VerifyExists $signTool

& $signTool sign /f $pfx $exePath
