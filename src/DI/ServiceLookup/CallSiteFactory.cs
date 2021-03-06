// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.Internal;

namespace Microsoft.Extensions.DependencyInjection.ServiceLookup
{
    internal class CallSiteFactory
    {
        private readonly List<ServiceDescriptor> _descriptors;
        private readonly Dictionary<Type, IServiceCallSite> _callSiteCache = new Dictionary<Type, IServiceCallSite>();
        private readonly Dictionary<Type, ServiceDescriptorCacheItem> _descriptorLookup = new Dictionary<Type, ServiceDescriptorCacheItem>();

        public CallSiteFactory(IEnumerable<ServiceDescriptor> descriptors)
        {
            _descriptors = descriptors.ToList();
            Populate(descriptors);
        }

        private void Populate(IEnumerable<ServiceDescriptor> descriptors)
        {
            foreach (var descriptor in descriptors)
            {
                var serviceTypeInfo = descriptor.ServiceType.GetTypeInfo();
                if (serviceTypeInfo.IsGenericTypeDefinition)
                {
                    var implementationTypeInfo = descriptor.ImplementationType?.GetTypeInfo();

                    if (implementationTypeInfo == null || !implementationTypeInfo.IsGenericTypeDefinition)
                    {
                        throw new ArgumentException(
                            Resources.FormatOpenGenericServiceRequiresOpenGenericImplementation(descriptor.ServiceType),
                            nameof(descriptors));
                    }

                    if (implementationTypeInfo.IsAbstract || implementationTypeInfo.IsInterface)
                    {
                        throw new ArgumentException(
                            Resources.FormatTypeCannotBeActivated(descriptor.ImplementationType, descriptor.ServiceType));
                    }
                }
                else if (descriptor.ImplementationInstance == null && descriptor.ImplementationFactory == null)
                {
                    Debug.Assert(descriptor.ImplementationType != null);
                    var implementationTypeInfo = descriptor.ImplementationType.GetTypeInfo();

                    if (implementationTypeInfo.IsGenericTypeDefinition ||
                        implementationTypeInfo.IsAbstract ||
                        implementationTypeInfo.IsInterface)
                    {
                        throw new ArgumentException(
                            Resources.FormatTypeCannotBeActivated(descriptor.ImplementationType, descriptor.ServiceType));
                    }
                }

                var cacheKey = descriptor.ServiceType;
                ServiceDescriptorCacheItem cacheItem;
                _descriptorLookup.TryGetValue(cacheKey, out cacheItem);
                _descriptorLookup[cacheKey] = cacheItem.Add(descriptor);
            }
        }

        internal IServiceCallSite CreateCallSite(Type serviceType, ISet<Type> callSiteChain)
        {
            lock (_callSiteCache)
            {
                IServiceCallSite cachedCallSite;
                if (_callSiteCache.TryGetValue(serviceType, out cachedCallSite))
                {
                    return cachedCallSite;
                }

                IServiceCallSite callSite;
                try
                {
                    // ISet.Add returns false if serviceType already present in call Site Chain
                    if (!callSiteChain.Add(serviceType))
                    {
                        throw new InvalidOperationException(Resources.FormatCircularDependencyException(serviceType));
                    }

                    callSite = TryCreateExact(serviceType, callSiteChain) ??
                               TryCreateOpenGeneric(serviceType, callSiteChain) ??
                               TryCreateEnumerable(serviceType, callSiteChain);
                }
                finally
                {
                    callSiteChain.Remove(serviceType);
                }

                _callSiteCache[serviceType] = callSite;

                return callSite;
            }
        }

        private IServiceCallSite TryCreateExact(Type serviceType, ISet<Type> callSiteChain)
        {
            ServiceDescriptorCacheItem descriptor;
            if (_descriptorLookup.TryGetValue(serviceType, out descriptor))
            {
                return TryCreateExact(descriptor.Last, serviceType, callSiteChain);
            }

            return null;
        }

        private IServiceCallSite TryCreateOpenGeneric(Type serviceType, ISet<Type> callSiteChain)
        {
            ServiceDescriptorCacheItem descriptor;
            if (serviceType.IsConstructedGenericType
                && _descriptorLookup.TryGetValue(serviceType.GetGenericTypeDefinition(), out descriptor))
            {
                return TryCreateOpenGeneric(descriptor.Last, serviceType, callSiteChain);
            }

            return null;
        }

        private IServiceCallSite TryCreateEnumerable(Type serviceType, ISet<Type> callSiteChain)
        {
            if (serviceType.IsConstructedGenericType &&
                serviceType.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            {
                var itemType = serviceType.GenericTypeArguments.Single();

                var callSites = new List<IServiceCallSite>();

                // If item type is not generic we can safely use descriptor cache
                ServiceDescriptorCacheItem descriptors;
                if (!itemType.IsConstructedGenericType &&
                    _descriptorLookup.TryGetValue(itemType, out descriptors))
                {
                    for (int i = 0; i < descriptors.Count; i++)
                    {
                        var descriptor = descriptors[i];

                        // There may not be any open generics here
                        var callSite = TryCreateExact(descriptor, itemType, callSiteChain);
                        Debug.Assert(callSite != null);

                        callSites.Add(callSite);
                    }
                }
                else
                {
                    foreach (var descriptor in _descriptors)
                    {
                        var callSite = TryCreateExact(descriptor, itemType, callSiteChain) ??
                                       TryCreateOpenGeneric(descriptor, itemType, callSiteChain);

                        if (callSite != null)
                        {
                            callSites.Add(callSite);
                        }
                    }

                }

                return new IEnumerableCallSite(itemType, callSites.ToArray());
            }

            return null;
        }

        private IServiceCallSite TryCreateExact(ServiceDescriptor descriptor, Type serviceType, ISet<Type> callSiteChain)
        {
            if (serviceType == descriptor.ServiceType)
            {
                IServiceCallSite callSite;
                if (descriptor.ImplementationInstance != null)
                {
                    callSite = new ConstantCallSite(descriptor.ServiceType, descriptor.ImplementationInstance);
                }
                else if (descriptor.ImplementationFactory != null)
                {
                    callSite = new FactoryCallSite(descriptor.ServiceType, descriptor.ImplementationFactory);
                }
                else if (descriptor.ImplementationType != null)
                {
                    callSite = CreateConstructorCallSite(descriptor.ServiceType, descriptor.ImplementationType, callSiteChain);
                }
                else
                {
                    throw new InvalidOperationException("Invalid service descriptor");
                }

                return ApplyLifetime(callSite, descriptor, descriptor.Lifetime);
            }

            return null;
        }

        private IServiceCallSite TryCreateOpenGeneric(ServiceDescriptor descriptor, Type serviceType, ISet<Type> callSiteChain)
        {
            if (serviceType.IsConstructedGenericType &&
                serviceType.GetGenericTypeDefinition() == descriptor.ServiceType)
            {
                Debug.Assert(descriptor.ImplementationType != null, "descriptor.ImplementationType != null");

                var closedType = descriptor.ImplementationType.MakeGenericType(serviceType.GenericTypeArguments);
                var constructorCallSite = CreateConstructorCallSite(serviceType, closedType, callSiteChain);

                return ApplyLifetime(constructorCallSite, Tuple.Create(descriptor, serviceType), descriptor.Lifetime);
            }

            return null;
        }

        private IServiceCallSite ApplyLifetime(IServiceCallSite serviceCallSite, object cacheKey, ServiceLifetime descriptorLifetime)
        {
            if (serviceCallSite is ConstantCallSite)
            {
                return serviceCallSite;
            }

            switch (descriptorLifetime)
            {
                case ServiceLifetime.Transient:
                    return new TransientCallSite(serviceCallSite);
                case ServiceLifetime.Scoped:
                    return new ScopedCallSite(serviceCallSite, cacheKey);
                case ServiceLifetime.Singleton:
                    return new SingletonCallSite(serviceCallSite, cacheKey);
                default:
                    throw new ArgumentOutOfRangeException(nameof(descriptorLifetime));
            }
        }

        private IServiceCallSite CreateConstructorCallSite(Type serviceType, Type implementationType, ISet<Type> callSiteChain)
        {
            var constructors = implementationType.GetTypeInfo()
                .DeclaredConstructors
                .Where(constructor => constructor.IsPublic)
                .ToArray();

            IServiceCallSite[] parameterCallSites = null;

            if (constructors.Length == 0)
            {
                throw new InvalidOperationException(Resources.FormatNoConstructorMatch(implementationType));
            }
            else if (constructors.Length == 1)
            {
                var constructor = constructors[0];
                var parameters = constructor.GetParameters();
                if (parameters.Length == 0)
                {
                    return new CreateInstanceCallSite(serviceType, implementationType);
                }

                parameterCallSites = CreateArgumentCallSites(
                    serviceType,
                    implementationType,
                    callSiteChain,
                    parameters,
                    throwIfCallSiteNotFound: true);

                return new ConstructorCallSite(serviceType, constructor, parameterCallSites);
            }

            Array.Sort(constructors,
                (a, b) => b.GetParameters().Length.CompareTo(a.GetParameters().Length));

            ConstructorInfo bestConstructor = null;
            HashSet<Type> bestConstructorParameterTypes = null;
            for (var i = 0; i < constructors.Length; i++)
            {
                var parameters = constructors[i].GetParameters();

                var currentParameterCallSites = CreateArgumentCallSites(
                    serviceType,
                    implementationType,
                    callSiteChain,
                    parameters,
                    throwIfCallSiteNotFound: false);

                if (currentParameterCallSites != null)
                {
                    if (bestConstructor == null)
                    {
                        bestConstructor = constructors[i];
                        parameterCallSites = currentParameterCallSites;
                    }
                    else
                    {
                        // Since we're visiting constructors in decreasing order of number of parameters,
                        // we'll only see ambiguities or supersets once we've seen a 'bestConstructor'.

                        if (bestConstructorParameterTypes == null)
                        {
                            bestConstructorParameterTypes = new HashSet<Type>(
                                bestConstructor.GetParameters().Select(p => p.ParameterType));
                        }

                        if (!bestConstructorParameterTypes.IsSupersetOf(parameters.Select(p => p.ParameterType)))
                        {
                            // Ambigious match exception
                            var message = string.Join(
                                Environment.NewLine,
                                Resources.FormatAmbigiousConstructorException(implementationType),
                                bestConstructor,
                                constructors[i]);
                            throw new InvalidOperationException(message);
                        }
                    }
                }
            }

            if (bestConstructor == null)
            {
                throw new InvalidOperationException(
                    Resources.FormatUnableToActivateTypeException(implementationType));
            }
            else
            {
                Debug.Assert(parameterCallSites != null);
                return parameterCallSites.Length == 0 ?
                    (IServiceCallSite)new CreateInstanceCallSite(serviceType, implementationType) :
                    new ConstructorCallSite(serviceType, bestConstructor, parameterCallSites);
            }
        }

        private IServiceCallSite[] CreateArgumentCallSites(
            Type serviceType,
            Type implementationType,
            ISet<Type> callSiteChain,
            ParameterInfo[] parameters,
            bool throwIfCallSiteNotFound)
        {
            var parameterCallSites = new IServiceCallSite[parameters.Length];
            for (var index = 0; index < parameters.Length; index++)
            {
                var callSite = CreateCallSite(parameters[index].ParameterType, callSiteChain);

                object defaultValue;
                if (callSite == null && ParameterDefaultValue.TryGetDefaultValue(parameters[index], out defaultValue))
                {
                    callSite = new ConstantCallSite(serviceType, defaultValue);
                }

                if (callSite == null)
                {
                    if (throwIfCallSiteNotFound)
                    {
                        throw new InvalidOperationException(Resources.FormatCannotResolveService(
                            parameters[index].ParameterType,
                            implementationType));
                    }

                    return null;
                }

                parameterCallSites[index] = callSite;
            }

            return parameterCallSites;
        }


        public void Add(Type type, IServiceCallSite serviceCallSite)
        {
            _callSiteCache[type] = serviceCallSite;
        }

        private struct ServiceDescriptorCacheItem
        {
            private ServiceDescriptor _item;
            private List<ServiceDescriptor> _items;

            public ServiceDescriptor Last
            {
                get
                {
                    if (_items != null && _items.Count > 0)
                    {
                        return _items[_items.Count - 1];
                    }

                    Debug.Assert(_item != null);
                    return _item;
                }
            }

            public int Count
            {
                get
                {
                    if (_item == null)
                    {
                        Debug.Assert(_items == null);
                        return 0;
                    }

                    return 1 + (_items?.Count ?? 0);
                }
            }

            public ServiceDescriptor this[int index]
            {
                get
                {
                    if (index >= Count)
                    {
                        throw new ArgumentOutOfRangeException(nameof(index));
                    }

                    if (index == 0)
                    {
                        return _item;
                    }

                    return _items[index - 1];
                }
            }

            public ServiceDescriptorCacheItem Add(ServiceDescriptor descriptor)
            {
                var newCacheItem = new ServiceDescriptorCacheItem();
                if (_item == null)
                {
                    Debug.Assert(_items == null);
                    newCacheItem._item = descriptor;
                }
                else
                {
                    newCacheItem._item = _item;
                    newCacheItem._items = _items ?? new List<ServiceDescriptor>();
                    newCacheItem._items.Add(descriptor);
                }
                return newCacheItem;
            }
        }
    }

    internal class Resources
    {
        public static string FormatOpenGenericServiceRequiresOpenGenericImplementation(Type serviceType)
        {
            return
                $"Open generic service type '{serviceType}' requires registering an open generic implementation type.";
        }

        public static string FormatTypeCannotBeActivated(Type implementationType, Type serviceType)
        {
            return $"Cannot instantiate implementation type '{implementationType}' for service type '{serviceType}'.";
        }

        public static string FormatCircularDependencyException(Type serviceType)
        {
            return $"A circular dependency was detected for the service of type '{serviceType}'.";
        }

        public static string FormatNoConstructorMatch(Type implementationType)
        {
            return $"A suitable constructor for type '{implementationType}' could not be located. " +
                   $"Ensure the type is concrete and services are registered for all parameters of a public constructor.";
        }

        public static string FormatAmbigiousConstructorException(Type implementationType)
        {
            return $"Unable to activate type '{implementationType}'. The following constructors are ambigious:";
        }

        public static string FormatUnableToActivateTypeException(Type implementationType)
        {
            return
                $"No constructor for type '{implementationType}' can be instantiated using services from the service container and default values.";
        }

        public static string FormatCannotResolveService(Type parameterType, Type implementationType)
        {
            return
                $"Unable to resolve service for type '{parameterType}' while attempting to activate '{implementationType}'.";
        }

        public static string FormatDirectScopedResolvedFromRootException(Type serviceType, string toLowerInvariant)
        {
            return $"Cannot resolve {toLowerInvariant} service '{serviceType}' from root provider.";
        }

        public static string FormatScopedInSingletonException(Type serviceType, Type type, string toLowerInvariant, string s)
        {
            return $"Cannot consume {toLowerInvariant} service '{serviceType}' from {s} '{type}'.";
        }

        public static string FormatScopedResolvedFromRootException(Type serviceType, Type scopedService, string toLowerInvariant)
        {
            return
                $"Cannot resolve '{serviceType}' from root provider because it requires {toLowerInvariant} service '{scopedService}'.";
        }
    }
}
