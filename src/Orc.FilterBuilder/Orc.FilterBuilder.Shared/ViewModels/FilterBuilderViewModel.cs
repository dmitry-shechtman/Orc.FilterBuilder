﻿// --------------------------------------------------------------------------------------------------------------------
// <copyright file="FilterBuilderControlModel.cs" company="Orcomp development team">
//   Copyright (c) 2008 - 2014 Orcomp development team. All rights reserved.
// </copyright>
// --------------------------------------------------------------------------------------------------------------------


namespace Orc.FilterBuilder.ViewModels
{
    using System;
    using System.Collections;
    using System.Collections.ObjectModel;
    using System.Collections.Specialized;
    using System.Linq;
    using System.Threading.Tasks;
    using Catel;
    using Catel.Collections;
    using Catel.Data;
    using Catel.IoC;
    using Catel.Logging;
    using Catel.MVVM;
    using Catel.Reflection;
    using Catel.Services;
    using Models;
    using Services;
    using Views;
    using CollectionHelper = Orc.FilterBuilder.CollectionHelper;

    public class FilterBuilderViewModel : ViewModelBase
    {
        private readonly ILog Log = LogManager.GetCurrentClassLogger();

        private readonly IUIVisualizerService _uiVisualizerService;
        private IFilterSchemeManager _filterSchemeManager;
        private IFilterService _filterService;
        private readonly IMessageService _messageService;
        private readonly IServiceLocator _serviceLocator;

        private readonly FilterScheme NoFilterFilter = new FilterScheme(typeof(object), "Default");
        private Type _targetType;
        private FilterSchemes _filterSchemes;
        private bool _applyingFilter;

        #region Constructors
        public FilterBuilderViewModel(IUIVisualizerService uiVisualizerService, IFilterSchemeManager filterSchemeManager,
            IFilterService filterService, IMessageService messageService, IServiceLocator serviceLocator)
        {
            Argument.IsNotNull(() => uiVisualizerService);
            Argument.IsNotNull(() => filterSchemeManager);
            Argument.IsNotNull(() => filterService);
            Argument.IsNotNull(() => messageService);
            Argument.IsNotNull(() => serviceLocator);

            _uiVisualizerService = uiVisualizerService;
            _filterSchemeManager = filterSchemeManager;
            _filterService = filterService;
            _messageService = messageService;
            _serviceLocator = serviceLocator;

            NewSchemeCommand = new Command(OnNewSchemeExecute);
            EditSchemeCommand = new TaskCommand<FilterScheme>(OnEditSchemeExecuteAsync, OnEditSchemeCanExecute);
            ApplySchemeCommand = new TaskCommand(OnApplySchemeExecuteAsync, OnApplySchemeCanExecute);
            ResetSchemeCommand = new Command(OnResetSchemeExecute, OnResetSchemeCanExecute);
            DeleteSchemeCommand = new Command<FilterScheme>(OnDeleteSchemeExecute, OnDeleteSchemeCanExecute);
        }
        #endregion

        #region Properties
        public ObservableCollection<FilterScheme> AvailableSchemes { get; private set; }
        public FilterScheme SelectedFilterScheme { get; set; }

        public bool AllowLivePreview { get; set; }
        public bool EnableAutoCompletion { get; set; }
        public bool AutoApplyFilter { get; set; }
        public bool AllowReset { get; set; }
        public bool AllowDelete { get; set; }

        public IEnumerable RawCollection { get; set; }
        public IList FilteredCollection { get; set; }

        /// <summary>
        /// Current <see cref="FilterBuilderControl"/> mode
        /// </summary>
        public FilterBuilderMode Mode { get; set; }

        /// <summary>
        /// Filtering function if <see cref="FilterBuilderControl"/> mode is 
        /// <see cref="FilterBuilderMode.FilteringFunction"/>
        /// </summary>
        public Func<object, bool> FilteringFunc { get; set; }

        public object ManagerTag { get; set; }
        #endregion

        #region Commands
        public Command NewSchemeCommand { get; private set; }

        private void OnNewSchemeExecute()
        {
            if (_targetType == null)
            {
                Log.Warning("Target type is unknown, cannot get any type information to create filters");
                return;
            }

            var filterScheme = new FilterScheme(_targetType);
            var filterSchemeEditInfo = new FilterSchemeEditInfo(filterScheme, RawCollection, AllowLivePreview, EnableAutoCompletion);

            if (_uiVisualizerService.ShowDialog<EditFilterViewModel>(filterSchemeEditInfo) ?? false)
            {
                AvailableSchemes.Add(filterScheme);
                _filterSchemes.Schemes.Add(filterScheme);

                ApplyFilterScheme(filterScheme, true);

                _filterSchemeManager.UpdateFilters();
            }
        }

        public TaskCommand<FilterScheme> EditSchemeCommand { get; private set; }

        private bool OnEditSchemeCanExecute(FilterScheme filterScheme)
        {
            if (filterScheme == null)
            {
                return false;
            }

            if (AvailableSchemes.Count == 0)
            {
                return false;
            }

            if (ReferenceEquals(AvailableSchemes[0], filterScheme))
            {
                return false;
            }

            return true;
        }

        private async Task OnEditSchemeExecuteAsync(FilterScheme filterScheme)
        {
            await filterScheme.EnsureIntegrityAsync();

            var filterSchemeEditInfo = new FilterSchemeEditInfo(filterScheme, RawCollection, AllowLivePreview, EnableAutoCompletion);

            if (_uiVisualizerService.ShowDialog<EditFilterViewModel>(filterSchemeEditInfo) ?? false)
            {
                _filterSchemeManager.UpdateFilters();

                ApplyFilter();
            }
        }

        public TaskCommand ApplySchemeCommand { get; private set; }

        private bool OnApplySchemeCanExecute()
        {
            if (SelectedFilterScheme == null)
            {
                return false;
            }

            if (RawCollection == null)
            {
                return false;
            }

            if (FilteredCollection == null && Mode == FilterBuilderMode.Collection)
            {
                return false;
            }

            return true;
        }

        private async Task OnApplySchemeExecuteAsync()
        {
            Log.Debug("Applying filter scheme '{0}'", SelectedFilterScheme);

            //build filtered collection only if current mode is Collection
            if (Mode == FilterBuilderMode.Collection)
            {
                FilteringFunc = null;
                await _filterService.FilterCollectionAsync(SelectedFilterScheme, RawCollection, FilteredCollection);
            }
            else
            {
                FilteringFunc = SelectedFilterScheme.CalculateResult;
            }
        }

        public Command ResetSchemeCommand { get; private set; }

        private bool OnResetSchemeCanExecute()
        {
            return AllowReset && ReadyForResetOrDeleteScheme(SelectedFilterScheme);
        }

        private void OnResetSchemeExecute()
        {
            if (AvailableSchemes.Count > 0)
            {
                SelectedFilterScheme = AvailableSchemes[0];
            }
        }

        public Command<FilterScheme> DeleteSchemeCommand { get; private set; }

        private bool OnDeleteSchemeCanExecute(FilterScheme filterScheme)
        {
            return AllowDelete && ReadyForResetOrDeleteScheme(filterScheme);
        }        

        private async void OnDeleteSchemeExecute(FilterScheme filterScheme)
        {
            if (await _messageService.ShowAsync(string.Format("Are you sure you want to delete filter '{0}'?", filterScheme.Title), "Delete filter?", MessageButton.YesNo) == MessageResult.Yes)
            {
                _filterSchemeManager.FilterSchemes.Schemes.Remove(filterScheme);

                SelectedFilterScheme = AvailableSchemes[0];

                _filterSchemeManager.UpdateFilters();
            }
        }
        #endregion

        #region Methods
        private bool ReadyForResetOrDeleteScheme(FilterScheme filterScheme)
        {
            return filterScheme != null && AvailableSchemes != null && AvailableSchemes.Any()
                   && !ReferenceEquals(filterScheme, AvailableSchemes[0]);
        }

        private void OnManagerTagChanged()
        {
            if (_filterSchemeManager != null)
            {
                _filterSchemeManager.Loaded -= OnFilterSchemeManagerLoaded;
                _filterService.SelectedFilterChanged -= OnFilterServiceSelectedFilterChanged;
            }

            _filterSchemeManager = _serviceLocator.ResolveType<IFilterSchemeManager>(ManagerTag);
            _filterSchemeManager.Loaded += OnFilterSchemeManagerLoaded;

            _filterService = _serviceLocator.ResolveType<IFilterService>(ManagerTag);
            _filterService.SelectedFilterChanged += OnFilterServiceSelectedFilterChanged;

            UpdateFilters();
        }

        private void ApplyFilterScheme(FilterScheme filterScheme, bool force = false)
        {
            if (filterScheme == null || _applyingFilter)
            {
                return;
            }

            _applyingFilter = true;

            var selectedFilterIsDifferent = !ReferenceEquals(SelectedFilterScheme, filterScheme);
            var filterServiceSelectedFilterIsDifferent = !ReferenceEquals(filterScheme, _filterService.SelectedFilter);

            if (selectedFilterIsDifferent)
            {
                SelectedFilterScheme = filterScheme;
            }

            if (filterServiceSelectedFilterIsDifferent)
            {
                _filterService.SelectedFilter = filterScheme;
            }

            if (force || selectedFilterIsDifferent || filterServiceSelectedFilterIsDifferent)
            {
                if (AutoApplyFilter)
                {
                    ApplyFilter();
                }
            }

            _applyingFilter = false;
        }

        private void OnSelectedFilterSchemeChanged()
        {
            ApplyFilterScheme(SelectedFilterScheme);
        }

        private void OnRawCollectionChanged()
        {
            Log.Debug("Raw collection changed");

            UpdateFilters();

            ApplyFilter();
        }

        private void OnFilteredCollectionChanged()
        {
            Log.Debug("Filtered collection changed");

            ApplyFilter();
        }

        private void ApplyFilter()
        {
            ApplySchemeCommand.Execute();
        }

        private void UpdateFilters()
        {
            Log.Debug("Updating filters");

            if (_filterSchemes != null)
            {
                _filterSchemes.Schemes.CollectionChanged -= OnFilterSchemesCollectionChanged;
            }

            _filterSchemes = _filterSchemeManager.FilterSchemes;

            if (_filterSchemes != null)
            {
                _filterSchemes.Schemes.CollectionChanged += OnFilterSchemesCollectionChanged;
            }

            var newSchemes = new ObservableCollection<FilterScheme>();

            if (RawCollection == null)
            {
                _targetType = null;
            }
            else
            {
                _targetType = CollectionHelper.GetTargetType(RawCollection);
                if (_targetType != null)
                {
                    newSchemes.AddRange((from scheme in _filterSchemes.Schemes
                                         where scheme.TargetType != null && _targetType.IsAssignableFromEx(scheme.TargetType)
                                         select scheme));
                }
            }

            newSchemes.Insert(0, NoFilterFilter);

            if (AvailableSchemes == null || !Catel.Collections.CollectionHelper.IsEqualTo(AvailableSchemes, newSchemes))
            {
                AvailableSchemes = newSchemes;

                var selectedFilter = _filterService.SelectedFilter ?? NoFilterFilter;
                SelectedFilterScheme = selectedFilter;
            }
        }

        protected override async Task InitializeAsync()
        {
            await base.InitializeAsync();

            _filterSchemeManager.Loaded += OnFilterSchemeManagerLoaded;
            _filterService.SelectedFilterChanged += OnFilterServiceSelectedFilterChanged;

            UpdateFilters();
        }

        protected override async Task CloseAsync()
        {
            if (_filterSchemes != null)
            {
                _filterSchemes.Schemes.CollectionChanged -= OnFilterSchemesCollectionChanged;
            }

            _filterSchemeManager.Loaded -= OnFilterSchemeManagerLoaded;
            _filterService.SelectedFilterChanged -= OnFilterServiceSelectedFilterChanged;

            await base.CloseAsync();
        }

        private void OnFilterSchemesCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            UpdateFilters();
        }

        private void OnFilterSchemeManagerLoaded(object sender, EventArgs eventArgs)
        {
            UpdateFilters();
        }

        private void OnFilterServiceSelectedFilterChanged(object sender, EventArgs e)
        {
            var newFilterScheme = _filterService.SelectedFilter ?? AvailableSchemes.First();
            ApplyFilterScheme(newFilterScheme);
        }
        #endregion
    }
}