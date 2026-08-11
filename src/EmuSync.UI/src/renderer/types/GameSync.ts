export interface GameSyncStatus {
    lastSyncedFrom?: string | null;
    lastSyncedAtUtc?: Date | null;
    latestWriteTimeUtc?: Date | null;
    localLatestWriteTimeUtc?: Date | null;
    requiresUpload: boolean;
    requiresDownload: boolean;
    localFolderPathIsUnset: boolean;
    localFolderPathExists: boolean;
    storageBytes: number;
    children?: ChildSyncStatus[];
}

export interface ChildSyncStatus {
    path: string;
    exists: boolean;
    latestWriteTimeUtc?: Date | null;
    errors: ChildSyncError[];
}

export interface ChildSyncError {
    stage: string;
    message: string;
}

export interface SyncProgress {
    inProgress: boolean;
    overallCompletionPercent: number | null;
    currentStage: string | null;
}
