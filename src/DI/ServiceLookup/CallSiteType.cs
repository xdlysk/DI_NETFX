namespace DI.ServiceLookup
{
    enum CallSiteType
    {
        FactoryCallSite,
        IEnumerableCallSite,
        ConstructorCallSite,
        TransientCallSite,
        SingletonCallSite,
        ScopedCallSite,
        ConstantCallSite,
        CreateInstanceCallSite,
        ServiceProviderCallSite,
        ServiceScopeFactoryCallSite,
        Unkown
    }
}
