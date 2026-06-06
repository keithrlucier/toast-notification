namespace ToastRevival.Api.Models;

public enum UserRole { Technician, Admin, SuperAdmin }
public enum DeviceStatus { Active, Inactive, Decommissioned }
public enum TemplateCategory { Announcement, Alert, ActionRequired, Reminder, Celebration, Maintenance, Custom }
public enum NotificationStatus { Queued, Sending, Sent, PartialFailure, Failed, PendingReview }
public enum DeliveryStatus { Pending, Delivered, Clicked, Dismissed, Failed }
public enum TargetType { Device, Group, All }
public enum AssetType { HeroImage, Logo, Icon }
// DC-M1: Dead enum — only BillingController still references this (Routes agent to remove).
// Do not add new usages. Will be deleted once BillingController reference is cleaned up.
[Obsolete("SubscriptionTier column has been dropped. Use BillingStatus and BillingPlanRules instead.")]
public enum SubscriptionTier { Standard }
public enum BillingStatus { Active, PastDue, Canceled, Trialing }
public enum ToastScenario { Default, Alarm, Reminder, IncomingCall, Urgent }
public enum ModerationDecision { Pass, Review, Block }
public enum TrialRequestStatus { Pending, Approved, Rejected }
public enum TrialUseCase
{
    MspClientCommunication,
    InternalItOperations,
    SecurityIncidentResponse,
    MaintenanceWindowNotices,
    ComplianceAuditEvidence,
    ProductEvaluation,
    Other,
}
