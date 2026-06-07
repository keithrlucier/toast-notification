namespace ToastRevival.Api.Models;

public enum UserRole { Technician, Admin, SuperAdmin }
// REL-004-R: PendingUninstall(3) is set when admin requests remote removal;
// Decommissioned(2) is only set after the endpoint confirms local uninstall
// OR after admin manually confirms via POST /api/devices/{id}/confirm-decommission.
// This prevents the dashboard from reporting removal before it is proved.
public enum DeviceStatus { Active, Inactive, Decommissioned, PendingUninstall }
public enum TemplateCategory { Announcement, Alert, ActionRequired, Reminder, Celebration, Maintenance, Custom }
public enum NotificationStatus { Queued, Sending, Sent, PartialFailure, Failed, PendingReview }
public enum DeliveryStatus { Pending, Delivered, Clicked, Dismissed, Failed }
public enum TargetType { Device, Group, All }
public enum AssetType { HeroImage, Logo, Icon }
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
