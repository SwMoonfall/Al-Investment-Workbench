namespace AIInvestmentWorkbench.Domain.Enums;
public enum ThesisStatus { Draft, Active, Warning, Invalidated, Closed }
public enum AssumptionStatus { Unknown, Normal, Warning, Triggered }
public enum KillConditionStatus { Normal, Warning, Triggered }
public enum ThresholdDirection { AtOrAbove, AtOrBelow }
public enum ThesisActor { HumanUser, System, AI }
