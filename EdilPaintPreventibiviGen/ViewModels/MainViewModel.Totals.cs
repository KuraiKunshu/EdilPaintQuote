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
    #region Totals & Item Updates
    private void OnItemsCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems != null)
        {
            foreach (Item item in e.NewItems)
            {
                item.PropertyChanged += (s, args) =>
                {
                    Debug.WriteLine($"Item changed: {args.PropertyName} = {(s as Item)?.IsSignificant}");
                    CalculateTotals();
                };
            }
        }

        UpdateItemSortOrders();
        CalculateTotals();
    }

    public void UpdateItemSortOrders()
    {
        for (int i = 0; i < Materials.Count; i++)
            Materials[i].SortOrder = i;
        for (int i = 0; i < Labors.Count; i++)
            Labors[i].SortOrder = i;
    }

    private void ApplyCustomerDiscounts(Customer customer)
    {
        MaterialDiscount = customer.MaterialDiscount;
        LaborDiscount = customer.LaborDiscount;
    }

    public void CalculateTotals()
    {
        var totals = _quoteCalculator.Calculate(Materials, Labors, MaterialDiscount, LaborDiscount, IvaType);
        Imponibile = totals.Imponibile;
        IvaTotale = totals.IvaTotale;
        TotaleGenerale = totals.TotaleGenerale;
    }
    #endregion
}

