namespace LegacyRenewalApp
{
    public interface IBillingService
    {
        void SaveInvoice(RenewalInvoice invoice);
        void SendEmail(string email, string subject, string body);
    }
}