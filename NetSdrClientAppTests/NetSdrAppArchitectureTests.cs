using System;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using NetSdrClientApp;
using NetSdrClientApp.Networking;
using NetSdrClientApp.Messages;
using NUnit.Framework;

namespace NetSdrClientAppTests
{
    [TestFixture]
    public class NetSdrClientArchitectureTests
    {
        private Assembly _assembly;

        [SetUp]
        public void Setup()
        {
            _assembly = typeof(NetSdrClient).Assembly;
        }

        [Test]
        public void TcpAndUdpInterfaces_AreImplementedByWrappers()
        {
            var impls = _assembly.GetTypes()
                .Where(t => typeof(ITcpClient).IsAssignableFrom(t) && t.IsClass && !t.IsAbstract)
                .Select(t => t.Name)
                .ToList();

            Assert.That(impls, Does.Contain("TcpClientWrapper"), "Expected a concrete implementation of ITcpClient named TcpClientWrapper");

            var udpImpls = _assembly.GetTypes()
                .Where(t => typeof(IUdpClient).IsAssignableFrom(t) && t.IsClass && !t.IsAbstract)
                .Select(t => t.Name)
                .ToList();

            Assert.That(udpImpls, Does.Contain("UdpClientWrapper"), "Expected a concrete implementation of IUdpClient named UdpClientWrapper");
        }

        [Test]
        public void NetSdrClient_Constructor_Uses_Interface_Abstractions()
        {
            var clientType = typeof(NetSdrClient);

            // Check constructors first
            bool ctorAcceptsInterfaces = clientType.GetConstructors(BindingFlags.Public | BindingFlags.Instance)
                .Any(ctor => ctor.GetParameters().Any(p => p.ParameterType == typeof(ITcpClient))
                             && ctor.GetParameters().Any(p => p.ParameterType == typeof(IUdpClient)));

            if (!ctorAcceptsInterfaces)
            {
                // Fallback: check for private fields typed as interfaces
                var fields = clientType.GetFields(BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
                bool fieldsUseInterfaces = fields.Any(f => f.FieldType == typeof(ITcpClient))
                                          && fields.Any(f => f.FieldType == typeof(IUdpClient));

                Assert.IsTrue(fieldsUseInterfaces, "NetSdrClient must depend on ITcpClient and IUdpClient (constructor parameters or fields).");
            }
            else
            {
                Assert.IsTrue(ctorAcceptsInterfaces);
            }
        }

        [Test]
        public void SystemNetSockets_Types_Are_Contained_In_Networking_Namespace()
        {
            var socketNamespaces = new[] { "System.Net.Sockets" };

            var typesThatReferenceSockets = new HashSet<Type>();

            foreach (var type in _assembly.GetTypes())
            {
                // inspect fields
                foreach (var f in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
                {
                    if (f.FieldType?.Namespace != null && socketNamespaces.Any(ns => f.FieldType.Namespace.StartsWith(ns)))
                        typesThatReferenceSockets.Add(type);
                }

                // inspect properties
                foreach (var p in type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
                {
                    if (p.PropertyType?.Namespace != null && socketNamespaces.Any(ns => p.PropertyType.Namespace.StartsWith(ns)))
                        typesThatReferenceSockets.Add(type);
                }

                // inspect methods (parameters and return types)
                foreach (var m in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
                {
                    if (m.ReturnType?.Namespace != null && socketNamespaces.Any(ns => m.ReturnType.Namespace.StartsWith(ns)))
                        typesThatReferenceSockets.Add(type);

                    foreach (var p in m.GetParameters())
                    {
                        if (p.ParameterType?.Namespace != null && socketNamespaces.Any(ns => p.ParameterType.Namespace.StartsWith(ns)))
                            typesThatReferenceSockets.Add(type);
                    }
                }
            }

            // All types that reference socket types should be in a Networking namespace
            var nonNetworking = typesThatReferenceSockets.Where(t => !(t.Namespace?.Contains("Networking") ?? false)).ToList();

            Assert.That(nonNetworking, Is.Empty, "Types referencing System.Net.Sockets must be contained in a Networking namespace. Found: " + string.Join(", ", nonNetworking.Select(t => t.FullName)));
        }

        [Test]
        public void NetSdrMessageHelper_Is_In_Messages_And_DoesNot_Use_Sockets()
        {
            var helperType = typeof(NetSdrMessageHelper);

            Assert.That(helperType.Namespace, Does.Contain("Messages"), "NetSdrMessageHelper should live in a Messages namespace.");

            // Ensure helper type does not reference socket types in its members
            var socketNamespaces = new[] { "System.Net.Sockets" };

            bool referencesSockets = helperType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance)
                .Any(f => f.FieldType?.Namespace != null && socketNamespaces.Any(ns => f.FieldType.Namespace.StartsWith(ns)))
                || helperType.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance)
                .Any(p => p.PropertyType?.Namespace != null && socketNamespaces.Any(ns => p.PropertyType.Namespace.StartsWith(ns)))
                || helperType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance)
                .Any(m => (m.ReturnType?.Namespace != null && socketNamespaces.Any(ns => m.ReturnType.Namespace.StartsWith(ns)))
                          || m.GetParameters().Any(pa => pa.ParameterType?.Namespace != null && socketNamespaces.Any(ns => pa.ParameterType.Namespace.StartsWith(ns))));

            Assert.IsFalse(referencesSockets, "NetSdrMessageHelper should not reference System.Net.Sockets types.");
        }
    }
}
