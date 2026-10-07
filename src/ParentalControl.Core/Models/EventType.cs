namespace ParentalControl.Core.Models;

public enum EventType
{
    LOGIN,
    LOGOUT,
    SLEEP,
    WAKE,
    LIMIT_REACHED,
    FORCED_LOGOUT,
    LOGIN_DENIED,
    CLOCK_TAMPER,
    SESSION_LOCKED,
    SESSION_UNLOCKED,
    SECURITY_ALERT,
    PROMPT_SENT,
    PROMPT_ANSWERED,
    PROMPT_TIMEOUT
}
