﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Ocelot.Errors;
using Ocelot.Responses;
using Ocelot.Values;

namespace Ocelot.LoadBalancer.LoadBalancers
{
    public class LeastConnectionLoadBalancer : ILoadBalancer
    {
        private readonly Func<Task<List<Service>>> _services;
        private readonly List<Lease> _leases;
        private readonly string _serviceName;

        public LeastConnectionLoadBalancer(Func<Task<List<Service>>> services, string serviceName)
        {
            _services = services;
            _serviceName = serviceName;
            _leases = new List<Lease>();
        }

        public async Task<Response<HostAndPort>> Lease()
        {
            var services = await _services.Invoke();

            if (services == null)
            {
                return new ErrorResponse<HostAndPort>(new List<Error>() { new ServicesAreNullError($"services were null for {_serviceName}") });
            }

            if (!services.Any())
            {
                return new ErrorResponse<HostAndPort>(new List<Error>() { new ServicesAreEmptyError($"services were empty for {_serviceName}") });
            }

            //todo - maybe this should be moved somewhere else...? Maybe on a repeater on seperate thread? loop every second and update or something?
            UpdateServices(services);

            var leaseWithLeastConnections = GetLeaseWithLeastConnections();

            _leases.Remove(leaseWithLeastConnections);

            leaseWithLeastConnections = AddConnection(leaseWithLeastConnections);

            _leases.Add(leaseWithLeastConnections);

            return new OkResponse<HostAndPort>(new HostAndPort(leaseWithLeastConnections.HostAndPort.DownstreamHost, leaseWithLeastConnections.HostAndPort.DownstreamPort));
        }

        public Response Release(HostAndPort hostAndPort)
        {
            var matchingLease = _leases.FirstOrDefault(l => l.HostAndPort.DownstreamHost == hostAndPort.DownstreamHost
                && l.HostAndPort.DownstreamPort == hostAndPort.DownstreamPort);

            if (matchingLease != null)
            {
                var replacementLease = new Lease(hostAndPort, matchingLease.Connections - 1);

                _leases.Remove(matchingLease);

                _leases.Add(replacementLease);
            }

            return new OkResponse();
        }

        private Lease AddConnection(Lease lease)
        {
            return new Lease(lease.HostAndPort, lease.Connections + 1);
        }

        private Lease GetLeaseWithLeastConnections()
        {
            //now get the service with the least connections?
            Lease leaseWithLeastConnections = null;

            for (var i = 0; i < _leases.Count; i++)
            {
                if (i == 0)
                {
                    leaseWithLeastConnections = _leases[i];
                }
                else
                {
                    if (_leases[i].Connections < leaseWithLeastConnections.Connections)
                    {
                        leaseWithLeastConnections = _leases[i];
                    }
                }
            }

            return leaseWithLeastConnections;
        }

        private Response UpdateServices(List<Service> services)
        {
            if (_leases.Count > 0)
            {
                var leasesToRemove = new List<Lease>();

                foreach (var lease in _leases)
                {
                    var match = services.FirstOrDefault(s => s.HostAndPort.DownstreamHost == lease.HostAndPort.DownstreamHost
                        && s.HostAndPort.DownstreamPort == lease.HostAndPort.DownstreamPort);

                    if (match == null)
                    {
                        leasesToRemove.Add(lease);
                    }
                }

                foreach (var lease in leasesToRemove)
                {
                    _leases.Remove(lease);
                }

                foreach (var service in services)
                {
                    var exists = _leases.FirstOrDefault(l => l.HostAndPort.ToString() == service.HostAndPort.ToString());

                    if (exists == null)
                    {
                        _leases.Add(new Lease(service.HostAndPort, 0));
                    }
                }
            }
            else
            {
                foreach (var service in services)
                {
                    _leases.Add(new Lease(service.HostAndPort, 0));
                }
            }

            return new OkResponse();
        }
    }
}
