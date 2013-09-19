using System;
using Autofac;
using Octopus.Platform.Deployment.Configuration;

namespace Octopus.Shared.Configuration
{
    public class ServerConfigurationModule : Module
    {
        protected override void Load(ContainerBuilder builder)
        {
            base.Load(builder);

            builder.RegisterType<OctopusServerStorageConfiguration>().As<IOctopusServerStorageConfiguration, IMasterKeyConfiguration>();
            builder.RegisterType<DeploymentProcessConfiguration>().As<IDeploymentProcessConfiguration>().SingleInstance();
            builder.RegisterType<WebPortalConfiguration>().As<IWebPortalConfiguration>().SingleInstance();
        }
    }
}