using Noggog;
using Noggog.WPF;
using ReactiveUI;
using System;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Windows;

namespace Synthesis.Bethesda.GUI.Views
{
    public class PatcherConfigViewBase : NoggogUserControl<PatcherVM> { }

    /// <summary>
    /// Interaction logic for PatcherConfigView.xaml
    /// </summary>
    public partial class PatcherConfigView : PatcherConfigViewBase
    {
        public PatcherConfigView()
        {
            InitializeComponent();
            this.WhenActivated(disposable =>
            {
                this.WhenAnyValue(x => x.ViewModel!.Name)
                    .BindToStrict(this, x => x.PatcherDetailName.Text)
                    .DisposeWith(disposable);
                this.WhenAnyValue(x => x.ViewModel)
                    .BindToStrict(this, x => x.PatcherIconDisplay.DataContext)
                    .DisposeWith(disposable);
                this.WhenAnyValue(x => x.ViewModel)
                    .BindToStrict(this, x => x.ConfigDetailPane.Content)
                    .DisposeWith(disposable);

                this.WhenAnyValue(x => x.ViewModel!.DeleteCommand)
                    .BindToStrict(this, x => x.DeleteButton.Command)
                    .DisposeWith(disposable);

                // Hacky setup to edit nickname when focused, but display display name when not
                // Need to polish and redeploy EditableTextBox instead sometime
                this.WhenAnyValue(x => x.PatcherDetailName.Text)
                    .Skip(1)
                    .FilterSwitch(this.WhenAnyValue(x => x.PatcherDetailName.IsFocused))
                    .Subscribe(x =>
                    {
                        if (this.ViewModel.TryGet(out var vm))
                        {
                            vm.Name = x;
                        }
                    })
                    .DisposeWith(disposable);
                this.WhenAnyValue(x => x.PatcherDetailName.IsKeyboardFocused)
                    .Select(focused =>
                    {
                        if (focused)
                        {
                            return this.WhenAnyValue(x => x.ViewModel!.Name);
                        }
                        else
                        {
                            return this.WhenAnyValue(x => x.ViewModel!.Name);
                        }
                    })
                    .Switch()
                    .DistinctUntilChanged()
                    .Subscribe(x => this.PatcherDetailName.Text = x)
                    .DisposeWith(disposable);

                this.WhenAnyValue(x => x.ViewModel!.State)
                    .Select(x => x.IsHaltingError ? Visibility.Visible : Visibility.Hidden)
                    .BindToStrict(this, x => x.ErrorGrid.Visibility)
                    .DisposeWith(disposable);
                this.WhenAnyValue(x => x.ViewModel!.State)
                    .Select(x => x.IsHaltingError ? Visibility.Visible : Visibility.Collapsed)
                    .BindToStrict(this, x => x.ErrorGlow.Visibility)
                    .DisposeWith(disposable);
                this.WhenAnyValue(x => x.ViewModel!.State)
                    .Select(x => x.RunnableState.Reason)
                    .BindToStrict(this, x => x.ErrorTextBlock.Text)
                    .DisposeWith(disposable);
            });
        }
    }
}
