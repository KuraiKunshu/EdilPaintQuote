using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using EdilPaintPreventibiviGen.Models;
using EdilPaintPreventibiviGen.Services;
using EdilPaintPreventibiviGen.Views;

namespace EdilPaintPreventibiviGen.ViewModels;
public partial class MainViewModel
{
    #region UI Actions
    public void OpenCustomerFolder()
    {
        try
        {
            if (SelectedCustomer == null)
            {
                MessageBox.Show("Seleziona un cliente prima di aprire la cartella.");
                return;
            }
            _storagePathService.OpenFolder(_storagePathService.BuildCustomerPdfFolder(SelectedCustomer.BusinessName, null));
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Impossibile aprire la cartella.\n\n{ex.Message}", "Errore apertura", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    public void OpenReferenceFolder()
    {
        try
        {
            if (SelectedCustomer == null)
            {
                MessageBox.Show("Seleziona un cliente prima di aprire la cartella.");
                return;
            }
            if (!IsSecondCustomerEnabled || SelectedSecondCustomer == null)
            {
                MessageBox.Show("Seleziona anche il riferimento prima di aprire la cartella.");
                return;
            }
            _storagePathService.OpenFolder(_storagePathService.BuildCustomerPdfFolder(
                SelectedCustomer.BusinessName, SelectedSecondCustomer.BusinessName));
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Impossibile aprire la cartella del riferimento.\n\n{ex.Message}", "Errore apertura", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task<bool> HandleVeluxLogin()
    {
        return await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            var win = new VeluxLoginWindow { Owner = Application.Current.MainWindow };
            return win.ShowDialog() == true;
        });
    }
    #endregion
}

