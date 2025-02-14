﻿using Exceptionless.Core.Extensions;
using Exceptionless.Core.Geo;
using Exceptionless.Core.Models;
using Exceptionless.Core.Pipeline;
using Microsoft.Extensions.Logging;

namespace Exceptionless.Core.Plugins.EventProcessor.Default;

[Priority(50)]
public sealed class GeoPlugin : EventProcessorPluginBase
{
    private readonly IGeoIpService _geoIpService;

    public GeoPlugin(IGeoIpService geoIpService, AppOptions options, ILoggerFactory loggerFactory) : base(options, loggerFactory)
    {
        _geoIpService = geoIpService;
    }

    public override Task EventBatchProcessingAsync(ICollection<EventContext> contexts)
    {
        var geoGroups = contexts.GroupBy(c => c.Event.Geo);

        var tasks = new List<Task>();
        foreach (var group in geoGroups)
        {
            if (GeoResult.TryParse(group.Key, out var result) && result is not null && result.IsValid())
            {
                group.ForEach(c => UpdateGeoAndLocation(c.Event, result, false));
                continue;
            }

            // The geo coordinates are all the same, set the location from the result of any of the ip addresses.
            if (!String.IsNullOrEmpty(group.Key))
            {
                var ips = group.SelectMany(c => c.Event.GetIpAddresses()).Union(new[] { group.First().EventPostInfo?.IpAddress }).Distinct().ToList();
                if (ips.Count > 0)
                    tasks.Add(UpdateGeoInformationAsync(group, ips));
                continue;
            }

            // Each event in the group could be a different user;
            foreach (var context in group)
            {
                var ips = context.Event.GetIpAddresses().Union(new[] { context.EventPostInfo?.IpAddress }).ToList();
                if (ips.Count > 0)
                    tasks.Add(UpdateGeoInformationAsync(context, ips));
            }
        }

        return Task.WhenAll(tasks);
    }


    private async Task UpdateGeoInformationAsync(EventContext context, IEnumerable<string?> ips)
    {
        var result = await GetGeoFromIpAddressesAsync(ips).AnyContext();
        UpdateGeoAndLocation(context.Event, result);
    }

    private async Task UpdateGeoInformationAsync(IEnumerable<EventContext> contexts, IEnumerable<string?> ips)
    {
        var result = await GetGeoFromIpAddressesAsync(ips).AnyContext();
        contexts.ForEach(c => UpdateGeoAndLocation(c.Event, result));
    }

    private void UpdateGeoAndLocation(PersistentEvent ev, GeoResult? result, bool isValidLocation = true)
    {
        ev.Geo = result?.ToString();

        if (result is not null && isValidLocation)
            ev.SetLocation(result.ToLocation());
        else
            ev.SetLocation(null);
    }

    private async Task<GeoResult?> GetGeoFromIpAddressesAsync(IEnumerable<string?> ips)
    {
        foreach (string? ip in ips)
        {
            if (String.IsNullOrEmpty(ip))
                continue;

            var result = await _geoIpService.ResolveIpAsync(ip.ToAddress()).AnyContext();
            if (result is null || !result.IsValid())
                continue;

            return result;
        }

        return null;
    }
}
