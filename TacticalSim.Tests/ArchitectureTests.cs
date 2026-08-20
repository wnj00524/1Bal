using System;
using System.Linq;
using System.Reflection;
using TacticalSim.Core.Simulation;
using Xunit;

namespace TacticalSim.Tests
{
    public class ArchitectureTests
    {
        private static readonly Assembly CoreAssembly = typeof(TurnResolver).Assembly;

        [Fact]
        public void CoreAssembly_ShouldNotReferenceUIOrPresentationAssemblies()
        {
            var referencedAssemblies = CoreAssembly.GetReferencedAssemblies();

            string[] forbiddenAssemblyPrefixes =
            {
                "Godot",
                "System.Drawing",
                "System.Windows",
                "PresentationFramework",
                "PresentationCore",
                "WindowsBase",
                "Microsoft.AspNetCore",
                "TacticalSim.GodotClient",
                "TacticalSim.ConsoleApp",
                "Avalonia",
                "ImGui",
                "Silk.NET",
                "OpenTK"
            };

            foreach (var assemblyRef in referencedAssemblies)
            {
                foreach (var forbidden in forbiddenAssemblyPrefixes)
                {
                    Assert.False(
                        assemblyRef.Name?.StartsWith(forbidden, StringComparison.OrdinalIgnoreCase) == true,
                        $"TacticalSim.Core must not reference presentation/UI assembly: {assemblyRef.Name}");
                }
            }
        }

        [Fact]
        public void CoreTypes_ShouldNotDependOnUINamespacesOrTypes()
        {
            var allTypes = CoreAssembly.GetTypes();

            string[] forbiddenNamespaceKeywords =
            {
                "Godot",
                "Drawing",
                "Windows.Forms",
                "UI",
                "Rendering",
                "Presentation"
            };

            foreach (var type in allTypes)
            {
                // Check base type
                if (type.BaseType != null)
                {
                    var baseNamespace = type.BaseType.Namespace ?? string.Empty;
                    foreach (var forbidden in forbiddenNamespaceKeywords)
                    {
                        Assert.False(
                            baseNamespace.Contains(forbidden, StringComparison.OrdinalIgnoreCase),
                            $"Type {type.FullName} inherits from forbidden presentation type {type.BaseType.FullName}");
                    }
                }

                // Check properties
                foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
                {
                    var propTypeNamespace = prop.PropertyType.Namespace ?? string.Empty;
                    foreach (var forbidden in forbiddenNamespaceKeywords)
                    {
                        Assert.False(
                            propTypeNamespace.Contains(forbidden, StringComparison.OrdinalIgnoreCase),
                            $"Property {type.Name}.{prop.Name} references forbidden namespace: {propTypeNamespace}");
                    }
                }

                // Check fields
                foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
                {
                    var fieldTypeNamespace = field.FieldType.Namespace ?? string.Empty;
                    foreach (var forbidden in forbiddenNamespaceKeywords)
                    {
                        Assert.False(
                            fieldTypeNamespace.Contains(forbidden, StringComparison.OrdinalIgnoreCase),
                            $"Field {type.Name}.{field.Name} references forbidden namespace: {fieldTypeNamespace}");
                    }
                }

                // Check methods return types and parameter types
                foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
                {
                    var returnNamespace = method.ReturnType.Namespace ?? string.Empty;
                    foreach (var forbidden in forbiddenNamespaceKeywords)
                    {
                        Assert.False(
                            returnNamespace.Contains(forbidden, StringComparison.OrdinalIgnoreCase),
                            $"Method {type.Name}.{method.Name} returns forbidden type namespace: {returnNamespace}");
                    }

                    foreach (var param in method.GetParameters())
                    {
                        var paramNamespace = param.ParameterType.Namespace ?? string.Empty;
                        foreach (var forbidden in forbiddenNamespaceKeywords)
                        {
                            Assert.False(
                                paramNamespace.Contains(forbidden, StringComparison.OrdinalIgnoreCase),
                                $"Method {type.Name}.{method.Name} parameter '{param.Name}' has forbidden type namespace: {paramNamespace}");
                        }
                    }
                }
            }
        }

        [Fact]
        public void CoreNamespaces_MustOnlyBeCoreOrStandard()
        {
            var allTypes = CoreAssembly.GetTypes();

            foreach (var type in allTypes)
            {
                if (type.Namespace != null)
                {
                    Assert.True(
                        type.Namespace.StartsWith("TacticalSim.Core", StringComparison.Ordinal) ||
                        type.Namespace.StartsWith("System", StringComparison.Ordinal),
                        $"Core type {type.FullName} has invalid namespace '{type.Namespace}'");
                }
            }
        }
    }
}
