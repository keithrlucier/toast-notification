namespace ToastRevival.Api.Models;

public enum UserRole { Technician, Admin, SuperAdmin }
public enum DeviceStatus { Active, Inactive, Decommissioned }
public enum TemplateCategory { Announcement, Alert, ActionRequired, Reminder, Celebration, Maintenance }
public enum NotificationStatus { Queued, Sending, Sent, PartialFailure, Failed }
public enum DeliveryStatus { Pending, Delivered, Clicked, Dismissed, Failed }
public enum TargetType { Device, Group, All }
public enum AssetType { HeroImage, Logo, Icon }
public enum SubscriptionTier { Free, Pro, Enterprise }
public enum BillingStatus { Active, PastDue, Canceled, Trialing }
public enum ToastScenario { Default, Alarm, Reminder, IncomingCall, Urgent }
