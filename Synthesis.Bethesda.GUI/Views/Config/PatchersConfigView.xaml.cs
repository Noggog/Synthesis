using Noggog.WPF;
using Noggog;
using ReactiveUI;
using System.Reactive.Disposables;
using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Reactive.Linq;
using System.Windows.Media;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Documents;
using DynamicData;

namespace Synthesis.Bethesda.GUI.Views
{
    public class PatchersConfigViewBase : NoggogUserControl<ConfigurationVM> { }

    /// <summary>
    /// Interaction logic for PatchersConfigurationView.xaml
    /// </summary>
    public partial class PatchersConfigView : PatchersConfigViewBase
    {
        public PatchersConfigView()
        {
            InitializeComponent();
            this.WhenActivated((disposable) =>
            {
                this.WhenAnyFallback(x => x.ViewModel!.SelectedProfile!.AddGitPatcherCommand, fallback: default(ICommand))
                    .BindToStrict(this, x => x.AddGitButton.Command)
                    .DisposeWith(disposable);
                this.WhenAnyFallback(x => x.ViewModel!.SelectedProfile!.AddSolutionPatcherCommand, fallback: default(ICommand))
                    .BindToStrict(this, x => x.AddSolutionButton.Command)
                    .DisposeWith(disposable);
                this.WhenAnyFallback(x => x.ViewModel!.SelectedProfile!.AddCliPatcherCommand, fallback: default(ICommand))
                    .BindToStrict(this, x => x.AddCliButton.Command)
                    .DisposeWith(disposable);
                this.WhenAnyFallback(x => x.ViewModel!.SelectedProfile!.AddSnippetPatcherCommand, fallback: default(ICommand))
                    .BindToStrict(this, x => x.AddSnippetButton.Command)
                    .DisposeWith(disposable);
                this.WhenAnyValue(x => x.ViewModel!.PatchersDisplay)
                    .BindToStrict(this, x => x.PatchersList.ItemsSource)
                    .DisposeWith(disposable);

                this.BindStrict(this.ViewModel, vm => vm.SelectedProfile!.DisplayedObject, view => view.PatchersList.SelectedItem)
                    .DisposeWith(disposable);

                // Wire up patcher config data context and visibility
                this.WhenAnyValue(x => x.ViewModel!.DisplayedObject)
                    .BindToStrict(this, x => x.DetailControl.Content)
                    .DisposeWith(disposable);

                // Only show help if zero patchers
                this.WhenAnyValue(x => x.ViewModel!.PatchersDisplay.Count)
                    .Select(c => c == 0 ? Visibility.Visible : Visibility.Collapsed)
                    .BindToStrict(this, x => x.AddSomePatchersHelpGrid.Visibility)
                    .DisposeWith(disposable);

                // Show dimmer if in initial configuration
                this.WhenAnyValue(x => x.ViewModel!.NewPatcher)
                    .Select(newPatcher => newPatcher != null ? Visibility.Visible : Visibility.Collapsed)
                    .BindToStrict(this, x => x.InitialConfigurationDimmer.Visibility)
                    .DisposeWith(disposable);

                // Set up go button
                this.WhenAnyValue(x => x.ViewModel!.RunPatchers)
                    .BindToStrict(this, x => x.GoButton.Command)
                    .DisposeWith(disposable);
                this.WhenAnyValue(x => x.ViewModel!.RunPatchers.CanExecute)
                    .Switch()
                    .Select(can => can ? Visibility.Visible : Visibility.Collapsed)
                    .BindToStrict(this, x => x.GoButton.Visibility)
                    .DisposeWith(disposable);
                this.WhenAnyValue(x => x.ViewModel!.RunPatchers.CanExecute)
                    .Switch()
                    .CombineLatest(this.WhenAnyFallback(x => x.ViewModel!.SelectedProfile!.LargeOverallError, GetResponse<PatcherVM>.Succeed(null!)),
                        (can, overall) => !can && overall.Succeeded)
                    .Select(show => show ? Visibility.Visible : Visibility.Collapsed)
                    .BindToStrict(this, x => x.ProcessingRingAnimation.Visibility)
                    .DisposeWith(disposable);

                // Set up large overall error button
                var overallErr = this.WhenAnyFallback(x => x.ViewModel!.SelectedProfile!.LargeOverallError, fallback: GetResponse<PatcherVM>.Succeed(null!))
                    .Replay(1)
                    .RefCount();
                Observable.CombineLatest(
                        this.WhenAnyValue(x => x.ViewModel!.PatchersDisplay.Count)
                            .Select(c => c > 0),
                        overallErr.Select(x => x.Succeeded),
                        (hasPatchers, succeeded) => hasPatchers && !succeeded)
                    .Select(x => x ? Visibility.Visible : Visibility.Collapsed)
                    .BindToStrict(this, x => x.OverallErrorButton.Visibility)
                    .DisposeWith(disposable);
                overallErr.Select(x => x.Reason)
                    .BindToStrict(this, x => x.OverallErrorButton.ToolTip)
                    .DisposeWith(disposable);
                this.WhenAnyFallback(x => x.ViewModel!.SelectedProfile!.GoToErrorCommand)
                    .BindToStrict(this, x => x.OverallErrorButton.Command)
                    .DisposeWith(disposable);

                Noggog.WPF.Drag.ListBoxDragDrop<PatcherVM>(this.PatchersList, () => this.ViewModel?.SelectedProfile?.Patchers)
                    .DisposeWith(disposable);

                // Bind top patcher list buttons
                this.WhenAnyValue(x => x.ViewModel!.PatchersDisplay.Count)
                    .Select(c => c == 0 ? Visibility.Hidden : Visibility.Visible)
                    .BindToStrict(this, x => x.TopAllPatchersControls.Visibility)
                    .DisposeWith(disposable);
                this.WhenAnyValue(x => x.ViewModel!.SelectedProfile!.EnableAllPatchersCommand)
                    .BindToStrict(this, x => x.EnableAllPatchersButton.Command)
                    .DisposeWith(disposable);
                this.WhenAnyValue(x => x.ViewModel!.SelectedProfile!.DisableAllPatchersCommand)
                    .BindToStrict(this, x => x.DisableAllPatchersButton.Command)
                    .DisposeWith(disposable);
                this.WhenAnyValue(x => x.ViewModel!.SelectedProfile!.UpdateAllPatchersCommand)
                    .BindToStrict(this, x => x.UpdateAllPatchersButton.Command)
                    .DisposeWith(disposable);
                this.WhenAnyValue(x => x.ViewModel!.SelectedProfile!.UpdateAllPatchersCommand)
                    .Select(x => x.CanExecute)
                    .Switch()
                    .Select(x => x ? Visibility.Visible : Visibility.Collapsed)
                    .BindToStrict(this, x => x.UpdateAllPatchersButton.Visibility)
                    .DisposeWith(disposable);
            });
        }
    }
}
