CREATE TABLE JobLocks (
    JobName NVARCHAR(100) PRIMARY KEY,  -- Unique identifier for each job
    IsRunning BIT NOT NULL DEFAULT 0,   -- Indicates whether the job is currently running (0 = No, 1 = Yes)
    LastRun DATETIME NULL               -- Timestamp of when the job was last run
);
