using UnityEngine;
using UnityEditor;
using System;
using System.IO;

public class PostProcessVisualStudioCSProject : AssetPostprocessor
{
	static public void OnGeneratedCSProjectFiles()
	{
		var projectDirectory = Directory.GetParent(Application.dataPath).FullName; ;
		var projectName = Path.GetFileName(projectDirectory);
		var slnFile = Path.Combine(projectDirectory, $"{projectName}.sln");
		var slnText = File.ReadAllText(slnFile);

		using (var sw = File.AppendText(slnFile))
		{
			void WriteProject(string name, string path)
			{
				if (!slnText.Contains($"{name}.Git"))
				{
					var guid = Guid.NewGuid();
					var projTypeGuid = "FAE04EC0-301F-11D3-BF4B-00C04F79EFBC";
					sw.WriteLine($@"Project(""{projTypeGuid}"") = ""{name}.Git"", ""{path}\{name}.Git.csproj"", ""}}""");
					sw.WriteLine("EndProject");
				}
			}

			WriteProject("Unmath", "Packages/com.arycama.unmath");
			WriteProject("CustomRenderPipeline", "Packages/com.arycama.customrenderpipeline");
		}

		//Debug.Log("Appended package projects to solution");
	}
}