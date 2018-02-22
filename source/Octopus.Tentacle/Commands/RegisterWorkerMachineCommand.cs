﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Octopus.Client;
using Octopus.Client.Operations;
using Octopus.Diagnostics;
using Octopus.Shared;
using Octopus.Shared.Configuration;
using Octopus.Tentacle.Commands.OptionSets;
using Octopus.Tentacle.Communications;
using Octopus.Tentacle.Configuration;

namespace Octopus.Tentacle.Commands
{
    public class RegisterWorkerMachineCommand : RegisterMachineCommandBase<IRegisterWorkerMachineOperation>
    {
        readonly List<string> workerpoolNames = new List<string>();


        public RegisterWorkerMachineCommand(Lazy<IRegisterWorkerMachineOperation> lazyRegisterMachineOperation,
            Lazy<ITentacleConfiguration> configuration,
            ILog log,
            IApplicationInstanceSelector selector,
            Lazy<IOctopusServerChecker> octopusServerChecker,
            IProxyConfigParser proxyConfig,
            IOctopusClientInitializer octopusClientInitializer)
            : base(lazyRegisterMachineOperation, configuration, log, selector, octopusServerChecker, proxyConfig, octopusClientInitializer)
        {
            Options.Add("workerpool=", "The worker pool name to add the machine to - e.g., 'Windows Pool'; specify this argument multiple times to add to multiple pools", s => workerpoolNames.Add(s));
        }

        protected override void CheckArgs()
        {
            if (workerpoolNames.Count == 0 || string.IsNullOrWhiteSpace(workerpoolNames.First()))
                throw new ControlledFailureException("Please specify a worker pool name, e.g., --workerpool=Default");
        }

        protected override void EnhanceOperation(IOctopusAsyncRepository repository, IRegisterWorkerMachineOperation registerOperation)
        {
            registerOperation.WorkerPoolNames = workerpoolNames.ToArray();
        }
    }
}