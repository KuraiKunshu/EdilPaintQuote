using System.Collections.Generic;

namespace EdilPaintPreventibiviGen.Models;

public class Company
{
    public string Nome { get; set; } = string.Empty;
    public string Indirizzo { get; set; } = string.Empty;
    public string Piva { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public List<string> Logo { get; set; } = new();
    public int Logo_index { get; set; }
    public int Counter { get; set; }
    public string Termini_pagamento { get; set; } = string.Empty;
}