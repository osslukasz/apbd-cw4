using System;

namespace LegacyRenewalApp
{
    public class SubscriptionRenewalService
    {
        private readonly ICustomerRepository _customerRepository;
        private readonly SubscriptionPlanRepository _planRepository;
        private readonly IBillingService _billingService;
        
        public SubscriptionRenewalService() : this(
            new CustomerRepository(), 
            new SubscriptionPlanRepository(), 
            new LegacyBillingAdapter())
        {
        }
        
        public SubscriptionRenewalService(
            ICustomerRepository customerRepository,
            SubscriptionPlanRepository planRepository,
            IBillingService billingService)
        {
            _customerRepository = customerRepository;
            _planRepository = planRepository;
            _billingService = billingService;
        }

        public RenewalInvoice CreateRenewalInvoice(
            int customerId,
            string planCode,
            int seatCount,
            string paymentMethod,
            bool includePremiumSupport,
            bool useLoyaltyPoints)
        {
            ValidateInput(customerId, planCode, seatCount, paymentMethod);

            var customer = _customerRepository.GetById(customerId);
            var plan = _planRepository.GetByCode(planCode.Trim().ToUpperInvariant());

            if (!customer.IsActive)
                throw new InvalidOperationException("Inactive customers cannot renew subscriptions");

            decimal baseAmount = (plan.MonthlyPricePerSeat * seatCount * 12m) + plan.SetupFee;
            
            var (discountAmount, discountNotes) = CalculateDiscounts(customer, plan, baseAmount, seatCount, useLoyaltyPoints);
            decimal subtotal = Math.Max(300m, baseAmount - discountAmount);

            decimal supportFee = includePremiumSupport ? CalculateSupportFee(plan.Code) : 0m;
            decimal paymentFee = CalculatePaymentFee(paymentMethod, subtotal + supportFee);
            decimal taxRate = GetTaxRate(customer.Country);

            decimal taxBase = subtotal + supportFee + paymentFee;
            decimal finalAmount = Math.Max(500m, taxBase * (1 + taxRate));

            var invoice = CreateInvoiceObject(customer, plan.Code, seatCount, paymentMethod, baseAmount, discountAmount, supportFee, paymentFee, taxBase * taxRate, finalAmount, discountNotes);
            
            _billingService.SaveInvoice(invoice);
            SendNotification(customer, invoice);

            return invoice;
        }

        private void ValidateInput(int customerId, string planCode, int seatCount, string paymentMethod)
        {
            if (customerId <= 0) throw new ArgumentException("Customer id must be positive");
            if (string.IsNullOrWhiteSpace(planCode)) throw new ArgumentException("Plan code is required");
            if (seatCount <= 0) throw new ArgumentException("Seat count must be positive");
            if (string.IsNullOrWhiteSpace(paymentMethod)) throw new ArgumentException("Payment method is required");
        }

        private (decimal Amount, string Notes) CalculateDiscounts(Customer customer, SubscriptionPlan plan, decimal baseAmount, int seatCount, bool useLoyaltyPoints)
        {
            decimal discount = 0;
            string notes = "";
            
            if (customer.Segment == "Silver") { discount += baseAmount * 0.05m; notes += "silver discount; "; }
            else if (customer.Segment == "Gold") { discount += baseAmount * 0.10m; notes += "gold discount; "; }
            else if (customer.Segment == "Platinum") { discount += baseAmount * 0.15m; notes += "platinum discount; "; }
            else if (customer.Segment == "Education" && plan.IsEducationEligible) { discount += baseAmount * 0.20m; notes += "education discount; "; }
            
            if (customer.YearsWithCompany >= 5) { discount += baseAmount * 0.07m; notes += "long-term loyalty discount; "; }
            else if (customer.YearsWithCompany >= 2) { discount += baseAmount * 0.03m; notes += "basic loyalty discount; "; }
            
            if (seatCount >= 50) { discount += baseAmount * 0.12m; notes += "large team discount; "; }
            else if (seatCount >= 20) { discount += baseAmount * 0.08m; notes += "medium team discount; "; }
            else if (seatCount >= 10) { discount += baseAmount * 0.04m; notes += "small team discount; "; }
            
            if (useLoyaltyPoints && customer.LoyaltyPoints > 0) 
            {
                int points = Math.Min(customer.LoyaltyPoints, 200);
                discount += points; notes += $"loyalty points used: {points}; ";
            }
            return (discount, notes);
        }

        private decimal CalculateSupportFee(string planCode) => planCode switch
        {
            "START" => 250m,
            "PRO" => 400m,
            "ENTERPRISE" => 700m,
            _ => 0m
        };

        private decimal CalculatePaymentFee(string method, decimal amount) => method.ToUpper() switch
        {
            "CARD" => amount * 0.02m,
            "BANK_TRANSFER" => amount * 0.01m,
            "PAYPAL" => amount * 0.035m,
            "INVOICE" => 0m,
            _ => throw new ArgumentException("Unsupported payment method")
        };

        private decimal GetTaxRate(string country) => country switch
        {
            "Poland" => 0.23m,
            "Germany" => 0.19m,
            "Czech Republic" => 0.21m,
            "Norway" => 0.25m,
            _ => 0.20m
        };

        private void SendNotification(Customer customer, RenewalInvoice invoice)
        {
            if (!string.IsNullOrWhiteSpace(customer.Email))
            {
                _billingService.SendEmail(customer.Email, "Subscription renewal invoice", 
                    $"Hello {customer.FullName}, your renewal for plan {invoice.PlanCode} has been prepared. Final amount: {invoice.FinalAmount:F2}.");
            }
        }

        private RenewalInvoice CreateInvoiceObject(Customer customer, string planCode, int seatCount, string paymentMethod, decimal baseAmt, decimal disc, decimal support, decimal payFee, decimal tax, decimal final, string notes)
        {
            return new RenewalInvoice
            {
                InvoiceNumber = $"INV-{DateTime.UtcNow:yyyyMMdd}-{customer.Id}-{planCode}",
                CustomerName = customer.FullName,
                PlanCode = planCode,
                PaymentMethod = paymentMethod,
                SeatCount = seatCount,
                BaseAmount = Math.Round(baseAmt, 2),
                DiscountAmount = Math.Round(disc, 2),
                SupportFee = Math.Round(support, 2),
                PaymentFee = Math.Round(payFee, 2),
                TaxAmount = Math.Round(tax, 2),
                FinalAmount = Math.Round(final, 2),
                Notes = notes.Trim(),
                GeneratedAt = DateTime.UtcNow
            };
        }
    }
}