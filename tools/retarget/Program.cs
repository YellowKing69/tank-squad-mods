using System;
using System.Linq;
using Mono.Cecil;
var asm = AssemblyDefinition.ReadAssembly(args[0]);
var mod = asm.MainModule;
foreach (var r in mod.AssemblyReferences)
    if (r.Version.Major == 8 && (r.Name.StartsWith("System") || r.Name == "netstandard" || r.Name.StartsWith("Microsoft")))
        r.Version = new Version(6, 0, 0, 0);
foreach (var ca in mod.CustomAttributes.Where(a => a.AttributeType.Name == "RefSafetyRulesAttribute").ToList())
    mod.CustomAttributes.Remove(ca);
var tfa = asm.CustomAttributes.FirstOrDefault(a => a.AttributeType.Name == "TargetFrameworkAttribute");
if (tfa != null)
{
    tfa.ConstructorArguments[0] = new CustomAttributeArgument(tfa.ConstructorArguments[0].Type, ".NETCoreApp,Version=v6.0");
    for (int i = 0; i < tfa.Properties.Count; i++)
        if (tfa.Properties[i].Name == "FrameworkDisplayName")
            tfa.Properties[i] = new CustomAttributeNamedArgument("FrameworkDisplayName", new CustomAttributeArgument(tfa.Properties[i].Argument.Type, ".NET 6.0"));
}
// Strip attributes that only exist publicly in .NET 7+ (compiler emits them when targeting net8).
string[] strip = { "NullableAttribute", "NullableContextAttribute", "NullablePublicOnlyAttribute", "RefSafetyRulesAttribute", "ScopedRefAttribute" };
bool Bad(CustomAttribute ca) => strip.Contains(ca.AttributeType.Name) && ca.AttributeType.Scope.Name.StartsWith("System");
int removed = 0;
void Clean(Mono.Collections.Generic.Collection<CustomAttribute> list) { foreach (var ca in list.Where(Bad).ToList()) { list.Remove(ca); removed++; } }
Clean(asm.CustomAttributes); Clean(mod.CustomAttributes);
foreach (var t in mod.GetTypes())
{
    Clean(t.CustomAttributes);
    foreach (var m in t.Methods) { Clean(m.CustomAttributes); Clean(m.MethodReturnType.CustomAttributes); foreach (var p in m.Parameters) Clean(p.CustomAttributes); }
    foreach (var f in t.Fields) Clean(f.CustomAttributes);
    foreach (var pr in t.Properties) Clean(pr.CustomAttributes);
    foreach (var g in t.GenericParameters) Clean(g.CustomAttributes);
}
Console.WriteLine("Stripped .NET7+ attributes: " + removed);
asm.Write(args[1]);
var chk = AssemblyDefinition.ReadAssembly(args[1]);
Console.WriteLine("Refs: " + string.Join(", ", chk.MainModule.AssemblyReferences.Select(r => r.Name + " " + r.Version)));
Console.WriteLine("RefSafety left: " + chk.MainModule.GetTypeReferences().Any(t => t.Name == "RefSafetyRulesAttribute"));
Console.WriteLine("Framework APIs used:");
foreach (var m in chk.MainModule.GetMemberReferences().Where(m => m.DeclaringType.Scope.Name.StartsWith("System"))
         .Select(m => "  " + m.DeclaringType.FullName + "::" + m.Name).Distinct().OrderBy(x => x)) Console.WriteLine(m);
