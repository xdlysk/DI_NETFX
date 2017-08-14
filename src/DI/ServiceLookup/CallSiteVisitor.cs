using System;
using DI.ServiceLookup;

namespace Microsoft.Extensions.DependencyInjection.ServiceLookup
{
    internal abstract class CallSiteVisitor<TArgument, TResult>
    {
        protected virtual TResult VisitCallSite(IServiceCallSite callSite, TArgument argument)
        {
            switch (callSite.CallSiteType)
            {
                case CallSiteType.FactoryCallSite:
                    return VisitFactory((FactoryCallSite)callSite, argument);
                case CallSiteType.IEnumerableCallSite:
                    return VisitIEnumerable((IEnumerableCallSite)callSite, argument);
                case CallSiteType.ConstructorCallSite:
                    return VisitConstructor((ConstructorCallSite)callSite, argument);
                case CallSiteType.TransientCallSite:
                    return VisitTransient((TransientCallSite)callSite, argument);
                case CallSiteType.SingletonCallSite:
                    return VisitSingleton((SingletonCallSite)callSite, argument);
                case CallSiteType.ScopedCallSite:
                    return VisitScoped((ScopedCallSite)callSite, argument);
                case CallSiteType.ConstantCallSite:
                    return VisitConstant((ConstantCallSite)callSite, argument);
                case CallSiteType.CreateInstanceCallSite:
                    return VisitCreateInstance((CreateInstanceCallSite)callSite, argument);
                case CallSiteType.ServiceProviderCallSite:
                    return VisitServiceProvider((ServiceProviderCallSite)callSite, argument);
                case CallSiteType.ServiceScopeFactoryCallSite:
                    return VisitServiceScopeFactory((ServiceScopeFactoryCallSite)callSite, argument);
                default:
                    throw new NotSupportedException($"Call site type {callSite.GetType()} is not supported");
            }

        }

        protected abstract TResult VisitTransient(TransientCallSite transientCallSite, TArgument argument);

        protected abstract TResult VisitConstructor(ConstructorCallSite constructorCallSite, TArgument argument);

        protected abstract TResult VisitSingleton(SingletonCallSite singletonCallSite, TArgument argument);

        protected abstract TResult VisitScoped(ScopedCallSite scopedCallSite, TArgument argument);

        protected abstract TResult VisitConstant(ConstantCallSite constantCallSite, TArgument argument);

        protected abstract TResult VisitCreateInstance(CreateInstanceCallSite createInstanceCallSite, TArgument argument);

        protected abstract TResult VisitServiceProvider(ServiceProviderCallSite serviceProviderCallSite, TArgument argument);

        protected abstract TResult VisitServiceScopeFactory(ServiceScopeFactoryCallSite serviceScopeFactoryCallSite, TArgument argument);

        protected abstract TResult VisitIEnumerable(IEnumerableCallSite enumerableCallSite, TArgument argument);

        protected abstract TResult VisitFactory(FactoryCallSite factoryCallSite, TArgument argument);
    }
}