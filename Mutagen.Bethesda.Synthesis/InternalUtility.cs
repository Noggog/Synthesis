using Mutagen.Bethesda.Internals;
using Mutagen.Bethesda.Synthesis.CLI;
using Noggog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BaseSynthesis = Synthesis.Bethesda;

namespace Mutagen.Bethesda.Synthesis.Internal
{
    public class Utility
    {
        public static SynthesisState<TMod, TModGetter> ToState<TMod, TModGetter>(RunSynthesisMutagenPatcher settings, UserPreferences userPrefs)
            where TMod : class, IMod, TModGetter
            where TModGetter : class, IModGetter
        {
            // Confirm target game release matches
            var regis = settings.GameRelease.ToCategory().ToModRegistration();
            if (!typeof(TMod).IsAssignableFrom(regis.SetterType))
            {
                throw new ArgumentException($"Target mod type {typeof(TMod)} was not of the expected type {regis.SetterType}");
            }
            if (!typeof(TModGetter).IsAssignableFrom(regis.GetterType))
            {
                throw new ArgumentException($"Target mod type {typeof(TModGetter)} was not of the expected type {regis.GetterType}");
            }

            // Get load order
            var loadOrderListing = SynthesisPipeline.Instance.GetLoadOrder(settings, userPrefs)
                .ToExtendedList();
            var rawLoadOrder = loadOrderListing.Select(x => new LoadOrderListing(x.ModKey, x.Enabled)).ToExtendedList();

            // Trim past Synthesis.esp
            var synthIndex = loadOrderListing.IndexOf(BaseSynthesis.Constants.SynthesisModKey, (listing, key) => listing.ModKey == key);
            if (synthIndex != -1)
            {
                loadOrderListing.RemoveToCount(synthIndex);
            }

            if (userPrefs.AddImplicitMasters)
            {
                AddImplicitMasters(settings, loadOrderListing);
            }

            // Remove disabled mods
            if (!userPrefs.IncludeDisabledMods)
            {
                loadOrderListing = loadOrderListing.OnlyEnabled().ToExtendedList();
            }

            var loadOrder = LoadOrder.Import<TModGetter>(
                settings.DataFolderPath,
                loadOrderListing,
                settings.GameRelease);

            // Get Modkey from output path
            var modKey = BaseSynthesis.Constants.SynthesisModKey;

            // Create or import patch mod
            TMod patchMod;
            ILinkCache cache;
            if (userPrefs.NoPatch)
            {
                // Pass null, even though it isn't normally
                patchMod = null!;

                TModGetter readOnlyPatchMod;
                if (settings.SourcePaths == null
                    || !settings.SourcePaths.Any())
                {
                    readOnlyPatchMod = ModInstantiator<TModGetter>.Activator(modKey, settings.GameRelease);
                }
                else if (settings.SourcePaths.CountGreaterThan(0))
                {
                    readOnlyPatchMod = ModInstantiator<TModGetter>.Importer(new ModPath(modKey, settings.SourcePaths.First()), settings.GameRelease);
                }
                else
                {
                    throw new NotImplementedException();
                }
                loadOrder.Add(new ModListing<TModGetter>(readOnlyPatchMod, enabled: true));
                cache = loadOrder.ToImmutableLinkCache();
            }
            else
            {
                if (settings.SourcePaths == null
                    || !settings.SourcePaths.Any())
                {
                    patchMod = ModInstantiator<TMod>.Activator(modKey, settings.GameRelease);
                }
                else if (settings.SourcePaths.CountGreaterThan(0))
                {
                    patchMod = ModInstantiator<TMod>.Importer(new ModPath(modKey, settings.SourcePaths.First()), settings.GameRelease);
                }
                else
                {
                    throw new NotImplementedException();
                }
                cache = loadOrder.ToMutableLinkCache(patchMod);
                loadOrder.Add(new ModListing<TModGetter>(patchMod, enabled: true));
            }

            return new SynthesisState<TMod, TModGetter>(
                settings: settings,
                loadOrder: loadOrder,
                rawLoadOrder: rawLoadOrder,
                linkCache: cache,
                patchMod: patchMod,
                extraDataPath: settings.ExtraDataFolder == null ? string.Empty : Path.GetFullPath(settings.ExtraDataFolder),
                cancellation: userPrefs.Cancel);
        }

        public static void AddImplicitMasters(RunSynthesisMutagenPatcher settings, ExtendedList<LoadOrderListing> loadOrderListing)
        {
            HashSet<ModKey> referencedMasters = new HashSet<ModKey>();
            foreach (var item in loadOrderListing.OnlyEnabled())
            {
                MasterReferenceReader reader = MasterReferenceReader.FromPath(Path.Combine(settings.DataFolderPath, item.ModKey.FileName), settings.GameRelease);
                referencedMasters.Add(reader.Masters.Select(m => m.Master));
            }
            for (int i = 0; i < loadOrderListing.Count; i++)
            {
                var listing = loadOrderListing[i];
                if (!listing.Enabled && referencedMasters.Contains(listing.ModKey))
                {
                    loadOrderListing[i] = new LoadOrderListing(listing.ModKey, enabled: true);
                }
            }
        }
    }
}
