namespace LegacyRenewalApp;

public interface ITaxProvider
{
    decimal GetTaxRate(string country);
}