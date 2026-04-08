namespace LegacyRenewalApp;

public interface IPaymentFeeCalculator
{
    decimal Calculate(string method, decimal amount);
}