namespace XcordHub.Entities;

public enum BillingStatus
{
    Active = 0,
    PastDue = 1,
    Suspended = 2,
    Cancelled = 3,

    /// <summary>
    /// Paid tier provisioned but no Stripe subscription exists yet. The
    /// BillingEnforcer suspends instances left in this state past the grace period.
    /// </summary>
    AwaitingPayment = 4
}
