using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using System.Xml;
using ICSharpCode.ILSpy;
using Mono.Cecil;
using Terraria.ModLoader.Properties;
using static Terraria.ModLoader.Setup.Program;
using ICSharpCode.Decompiler.CSharp;
using ICSharpCode.Decompiler.CSharp.OutputVisitor;
using ICSharpCode.Decompiler.TypeSystem;
using ICSharpCode.Decompiler.CSharp.Transforms;

namespace Terraria.ModLoader.Setup
{
	public class DecompileTask : Task
	{
		private class EmbeddedAssemblyResolver : BaseAssemblyResolver
		{
			private Dictionary<string, AssemblyDefinition> cache = new Dictionary<string, AssemblyDefinition>();
			public ModuleDefinition baseModule;

			public EmbeddedAssemblyResolver() {
				AddSearchDirectory(SteamDir);
			}

			public override AssemblyDefinition Resolve(AssemblyNameReference name, ReaderParameters parameters) {
				lock (this) {
					AssemblyDefinition assemblyDefinition;
					if (cache.TryGetValue(name.FullName, out assemblyDefinition))
						return assemblyDefinition;

					//ignore references to other mscorlib versions, they are unneeded and produce namespace conflicts
					if (name.Name == "mscorlib" && name.Version.Major != 4)
						goto skip;

					//look in the base module's embedded resources
					if (baseModule != null) {
						var resName = name.Name + ".dll";
						var res =
							baseModule.Resources.OfType<EmbeddedResource>()
								.SingleOrDefault(r => r.Name.EndsWith(resName));
						if (res != null)
							assemblyDefinition = AssemblyDefinition.ReadAssembly(res.GetResourceStream(), parameters);
					}

					if (assemblyDefinition == null)
						assemblyDefinition = base.Resolve(name, parameters);

				skip:
					cache[name.FullName] = assemblyDefinition;
					return assemblyDefinition;
				}
			}
		}

		private static readonly WholeProjectDecompiler wrappedDecompiler = new WholeProjectDecompiler();
		private static readonly Guid clientGuid = new Guid("3996D5FA-6E59-4FE4-9F2B-40EEEF9645D5");
		private static readonly Guid serverGuid = new Guid("85BF1171-A0DC-4696-BFA4-D6E9DC4E0830");
		public static readonly Version clientVersion = new Version(Settings.Default.ClientVersion);
		public static readonly Version serverVersion = new Version(Settings.Default.ServerVersion);

		public readonly string srcDir;
		public readonly bool serverOnly;

		public string FullSrcDir => Path.Combine(baseDir, srcDir);

		public DecompileTask(ITaskInterface taskInterface, string srcDir, bool serverOnly = false) : base(taskInterface) {
			this.srcDir = srcDir;
			this.serverOnly = serverOnly;
		}

		public override bool ConfigurationDialog() {
			if (File.Exists(TerrariaPath) && File.Exists(TerrariaServerPath))
				return true;

			return (bool)taskInterface.Invoke(new Func<bool>(SelectTerrariaDialog));
		}

		public override bool StartupWarning() {
			return MessageBox.Show(
					"Decompilation may take a long time (1-3 hours) and consume a lot of RAM (2GB will not be enough)",
					"Ready to Decompile", MessageBoxButtons.OKCancel, MessageBoxIcon.Information)
				== DialogResult.OK;
		}

		public override void Run() {
			taskInterface.SetStatus("Deleting Old Src");

			if (Directory.Exists(FullSrcDir))
				Directory.Delete(FullSrcDir, true);

			var items = new List<WorkItem>();

			var serverModule = ReadModule(TerrariaServerPath, serverVersion);
			var serverSources = GetCodeFiles(serverModule).ToList();
			var serverResources = GetResourceFiles(serverModule).ToList();

			var sources = serverSources;
			var resources = serverResources;
			var infoModule = serverModule;
			if (!serverOnly) {
				var clientModule = !serverOnly ? ReadModule(TerrariaPath, clientVersion) : null;
				var clientSources = GetCodeFiles(clientModule).ToList();
				var clientResources = GetResourceFiles(clientModule).ToList();

				sources = CombineFiles(clientSources, sources, src => src.Key);
				resources = CombineFiles(clientResources, resources, res => res.Item1);
				infoModule = clientModule;

				items.Add(new WorkItem("Writing Terraria.csproj",
					() => WriteProjectFile(clientModule, clientGuid, clientSources, clientResources)));

				items.Add(new WorkItem("Writing Terraria.csproj.user",
					() => WriteProjectUserFile(clientModule, SteamDir)));
			}

			items.Add(new WorkItem("Writing TerrariaServer.csproj",
				() => WriteProjectFile(serverModule, serverGuid, serverSources, serverResources)));

			items.Add(new WorkItem("Writing TerrariaServer.csproj.user",
				() => WriteProjectUserFile(serverModule, SteamDir)));

			DecompilerTypeSystem ts = new DecompilerTypeSystem(infoModule);

			items.Add(new WorkItem("Writing Assembly Info",
				() => WriteAssemblyInfo(infoModule, ts)));

			items.AddRange(sources.Select(src => new WorkItem(
				"Decompiling: "+src.Key, () => DecompileSourceFile(src, ts))));

			items.AddRange(resources.Select(res => new WorkItem(
				"Extracting: " + res.Item1, () => ExtractResource(res))));
			
			ExecuteParallel(items, maxDegree: Settings.Default.SingleDecompileThread ? 1 : 0);
		}

		protected ModuleDefinition ReadModule(string modulePath, Version version) {
			taskInterface.SetStatus("Loading "+Path.GetFileName(modulePath));
			var resolver = new EmbeddedAssemblyResolver();
			var module = ModuleDefinition.ReadModule(modulePath, 
				new ReaderParameters { AssemblyResolver = resolver});
			resolver.baseModule = module;
			
			if (module.Assembly.Name.Version != version)
				throw new Exception($"{module.Assembly.Name.Name} version {module.Assembly.Name.Version}. Expected {version}");

			return module;
		}

#region ReflectedMethods
		private static readonly MethodInfo _IncludeTypeWhenDecompilingProject = typeof(WholeProjectDecompiler)
			.GetMethod("IncludeTypeWhenDecompilingProject", BindingFlags.NonPublic | BindingFlags.Instance);

		public static bool IncludeTypeWhenDecompilingProject(TypeDefinition type) {
			return (bool)_IncludeTypeWhenDecompilingProject.Invoke(wrappedDecompiler, new object[] { type });
		}

		private static readonly MethodInfo _CreateDecompiler = typeof(WholeProjectDecompiler)
			.GetMethod("CreateDecompiler", BindingFlags.NonPublic | BindingFlags.Instance);

		public CSharpDecompiler CreateDecompiler(DecompilerTypeSystem ts) {
			var decompiler = (CSharpDecompiler)_CreateDecompiler.Invoke(wrappedDecompiler, new object[] { ts });
			decompiler.CancellationToken = taskInterface.CancellationToken();
			return decompiler;
		}

		private static readonly MethodInfo _WriteProjectFile = typeof(WholeProjectDecompiler)
			.GetMethod("WriteProjectFile", BindingFlags.NonPublic | BindingFlags.Instance);

		public static void WriteProjectFile(TextWriter writer, IEnumerable<Tuple<string, string>> files, ModuleDefinition module) {
			_WriteProjectFile.Invoke(wrappedDecompiler, new object[] { writer, files, module });
		}
#endregion

		//from ICSharpCode.ILSpy.CSharpLanguage
		private static IEnumerable<IGrouping<string, TypeDefinition>> GetCodeFiles(ModuleDefinition module) {
			return module.Types.Where(t => IncludeTypeWhenDecompilingProject(t))
				.GroupBy(type => {
					var file = WholeProjectDecompiler.CleanUpFileName(type.Name) + ".cs";
					return string.IsNullOrEmpty(type.Namespace) ? file : Path.Combine(WholeProjectDecompiler.CleanUpFileName(type.Namespace), file);
				}, StringComparer.OrdinalIgnoreCase);
		}

		private static IEnumerable<Tuple<string, EmbeddedResource>> GetResourceFiles(ModuleDefinition module) {
			return module.Resources.OfType<EmbeddedResource>().Select(res => {
				var path = res.Name;
				path = path.Replace("Terraria.Libraries.", "Terraria.Libraries\\");
				if (path.EndsWith(".dll")) {
					var asmRef = module.AssemblyReferences.SingleOrDefault(r => path.EndsWith(r.Name + ".dll"));
					if (asmRef != null)
						path = path.Substring(0, path.Length - asmRef.Name.Length - 5) +
						Path.DirectorySeparatorChar + asmRef.Name + ".dll";
				}
				return Tuple.Create(path, res);
			});
		}

		private static List<T> CombineFiles<T, K>(IEnumerable<T> client, IEnumerable<T> server, Func<T, K> key) {
			var list = client.ToList();
			var set = new HashSet<K>(list.Select(key));
			list.AddRange(server.Where(src => !set.Contains(key(src))));
			return list;
		}

		private void ExtractResource(Tuple<string, EmbeddedResource> res) {
			var path = Path.Combine(FullSrcDir, res.Item1);
			CreateParentDirectory(path);

			var s = res.Item2.GetResourceStream();
			s.Position = 0;
			using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write))
				s.CopyTo(fs);
		}

		private void DecompileSourceFile(IGrouping<string, TypeDefinition> src, DecompilerTypeSystem ts) {
			var path = Path.Combine(FullSrcDir, src.Key);
			CreateParentDirectory(path);

			using (var w = new StreamWriter(path)) {
				var decompiler = CreateDecompiler(ts);
				var syntaxTree = decompiler.DecompileTypes(src.ToArray());
				syntaxTree.AcceptVisitor(new CSharpOutputVisitor(w, wrappedDecompiler.Settings.CSharpFormattingOptions));
			}
		}

		private void WriteAssemblyInfo(ModuleDefinition module, DecompilerTypeSystem ts) {
			var path = Path.Combine(FullSrcDir, Path.Combine("Properties", "AssemblyInfo.cs"));
			CreateParentDirectory(path);

			using (var w = new StreamWriter(path)) {
				var decompiler = CreateDecompiler(ts);
				decompiler.AstTransforms.Add(new RemoveCompilerGeneratedAssemblyAttributes());
				var syntaxTree = decompiler.DecompileModuleAndAssemblyAttributes();
				syntaxTree.AcceptVisitor(new CSharpOutputVisitor(w, wrappedDecompiler.Settings.CSharpFormattingOptions));
			}
		}

		private void WriteProjectFile(ModuleDefinition module, Guid guid,
				IEnumerable<IGrouping<string, TypeDefinition>> sources, 
				IEnumerable<Tuple<string, EmbeddedResource>> resources) {

			//flatten the file list
			var files = sources.Select(src => Tuple.Create("Compile", src.Key))
				.Concat(resources.Select(res => Tuple.Create("EmbeddedResource", res.Item1)))
				.Concat(new[] { Tuple.Create("Compile", Path.Combine("Properties", "AssemblyInfo.cs")) });

			//fix the guid and add a value to the CommandLineArguments field so the method doesn't crash
			var claField = typeof(App).GetField("CommandLineArguments", BindingFlags.Static | BindingFlags.NonPublic);
			var claType = typeof(App).Assembly.GetType("ICSharpCode.ILSpy.CommandLineArguments");
			var claConstructor = claType.GetConstructors()[0];
			var claInst = claConstructor.Invoke(new object[] {Enumerable.Empty<string>()});
			var guidField = claType.GetField("FixedGuid");
			guidField.SetValue(claInst, guid);
			claField.SetValue(null, claInst);

			var path = Path.Combine(FullSrcDir,
				Path.GetFileNameWithoutExtension(module.Name) + ".csproj");
			CreateParentDirectory(path);

			using (var w = new StreamWriter(path))
				WriteProjectFile(w, files, module);
			using (var w = new StreamWriter(path, true))
				w.Write(Environment.NewLine);
		}

		private void WriteProjectUserFile(ModuleDefinition module, string debugWorkingDir) {
			var path = Path.Combine(FullSrcDir,
				Path.GetFileNameWithoutExtension(module.Name) + ".csproj.user");
			CreateParentDirectory(path);

			using (var w = new StreamWriter(path))
				using (var xml = new XmlTextWriter(w)) {
					xml.Formatting = Formatting.Indented;
					xml.WriteStartDocument();
					xml.WriteStartElement("Project", "http://schemas.microsoft.com/developer/msbuild/2003");
					xml.WriteAttributeString("ToolsVersion", "4.0");
					xml.WriteStartElement("PropertyGroup");
					xml.WriteAttributeString("Condition", "'$(Configuration)' == 'Debug'");
					xml.WriteStartElement("StartWorkingDirectory");
					xml.WriteValue(debugWorkingDir);
					xml.WriteEndElement();
					xml.WriteEndElement();
					xml.WriteEndDocument();
				}
		}
	}
}