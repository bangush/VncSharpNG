node('windows') {
	def jobDir = pwd()
	def vsToolsDir = "C:\\Program Files (x86)\\Microsoft Visual Studio 14.0\\Common7\\Tools"
	def vsExtensionsDir = "C:\\Program Files (x86)\\Microsoft Visual Studio 14.0\\Common7\\IDE\\CommonExtensions\\Microsoft\\TestWindow"
	
	stage 'Checkout Branch'
	def gitUrl = "https://github.com/mRemoteNG/VncSharpNG.git"
	def branchName = GetBranchName()
	echo "BranchName: ${branchName}"
	git([url: gitUrl, branch: branchName])

	stage 'Build VncSharpNG'
	bat "\"${vsToolsDir}\\VsDevCmd.bat\" && msbuild.exe /nologo \"${jobDir}\\VncSharp.sln\""
}
def GetBranchName() {
	def jobDir = pwd()
	echo "JobDir: ${jobDir}"
	def patternToUse = GetPatternToMatchBranchNameFromDirectory()
	echo "PatternToUse: ${patternToUse}"
	java.util.regex.Matcher matcher = jobDir =~ patternToUse
	echo "Ran the matcher"
	matcher ? matcher[0][1] : null
}
def GetPatternToMatchBranchNameFromDirectory() {
	def patternToUse = ""
	def folderSeparator = ""
	def branchNamePattern = "[a-zA-Z0-9\\-_]*"
	def jenkinsJobInfoTag = "@*[0-9]*"
	echo "isUnix: ${isUnix()}"
	if (isUnix()) {
		folderSeparator = "/"
	} else {
		folderSeparator =  "\\\\"
	}
	patternToUse = "${folderSeparator}(${branchNamePattern})(${jenkinsJobInfoTag})\$"
	patternToUse
}