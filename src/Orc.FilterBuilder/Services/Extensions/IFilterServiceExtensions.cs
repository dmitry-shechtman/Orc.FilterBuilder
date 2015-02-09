﻿// --------------------------------------------------------------------------------------------------------------------
// <copyright file="IFilterServiceExtensions.cs" company="Orcomp development team">
//   Copyright (c) 2008 - 2014 Orcomp development team. All rights reserved.
// </copyright>
// --------------------------------------------------------------------------------------------------------------------


namespace Orc.FilterBuilder.Services
{
    using System.Collections;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Models;

    public static class IFilterServiceExtensions
    {
        #region Methods
        public static async Task FilterCollectionAsync(this IFilterService filterService, FilterScheme filter, IEnumerable rawCollection, IList filteredCollection)
        {
            await Task.Factory.StartNew(() => filterService.FilterCollection(filter, rawCollection, filteredCollection));
        }

        public static async Task<IEnumerable<TItem>> FilterCollectionAsync<TItem>(this IFilterService filterService, FilterScheme filter, IEnumerable<TItem> rawCollection)
        {
            return await Task.Factory.StartNew(() => filterService.FilterCollection<TItem>(filter, rawCollection));
        }

        public static IEnumerable<TItem> FilterCollection<TItem>(this IFilterService filterService, FilterScheme filter, IEnumerable<TItem> rawCollection)
        {
            var filteredCollection = new List<TItem>();

            if (rawCollection == null)
            {
                return filteredCollection;
            }

            if (filter == null)
            {
                filteredCollection.AddRange(rawCollection);
                return filteredCollection;
            }

            filterService.FilterCollection(filter, rawCollection, filteredCollection);

            return filteredCollection;
        }

        public static IEnumerable<TItem> FilterCollectionWithCurrentFilter<TItem>(this IFilterService filterService, IEnumerable<TItem> rawCollection)
        {
            return filterService.FilterCollection(filterService.SelectedFilter, rawCollection);
        }
        #endregion
    }
}