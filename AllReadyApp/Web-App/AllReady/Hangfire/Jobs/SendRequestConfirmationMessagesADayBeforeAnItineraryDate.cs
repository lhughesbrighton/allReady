﻿using System;
using System.Collections.Generic;
using System.Linq;
using AllReady.Models;
using AllReady.Services;
using Hangfire;
using Microsoft.EntityFrameworkCore;

namespace AllReady.Hangfire.Jobs
{
    public class SendRequestConfirmationMessagesADayBeforeAnItineraryDate : ISendRequestConfirmationMessagesADayBeforeAnItineraryDate
    {
        private readonly AllReadyContext context;
        private readonly IBackgroundJobClient backgroundJob;
        private readonly ISmsSender smsSender;

        public Func<DateTime> DateTimeUtcNow = () => DateTime.UtcNow;

        public SendRequestConfirmationMessagesADayBeforeAnItineraryDate(AllReadyContext context, IBackgroundJobClient backgroundJob, ISmsSender smsSender)
        {
            this.context = context;
            this.backgroundJob = backgroundJob;
            this.smsSender = smsSender;
        }

        public void SendSms(List<Guid> requestIds, int itineraryId)
        {
            var requestorPhoneNumbers = context.Requests.Where(x => requestIds.Contains(x.RequestId) && x.Status == RequestStatus.PendingConfirmation).Select(x => x.Phone).ToList();
            if (requestorPhoneNumbers.Count > 0)
            {
                var itinerary = context.Itineraries.Include(i => i.Event).Single(x => x.Id == itineraryId);

                //don't send out messages if today is not 1 day before from the Itinerary.Date. This sceanrio can happen if:
                //1. a request is added to an itinereary less than 1 day away from the itinerary's date
                //2. if the Hangfire server is offline for the period where it would have tried to process this job. Hangfire processes jobs in the "past" by default
                if (TodayIsOneDayBeforeThe(itinerary.Date, itinerary.Event.TimeZoneId))
                {
                    smsSender.SendSmsAsync(requestorPhoneNumbers,
                        $@"Your request has been scheduled by allReady for {itinerary.Date.Date}. Please response with ""Y"" to confirm this request or ""N"" to cancel this request.");
                }

                //schedule job for day of Itinerary.Date
                //TODO: do we want to send out the "day of" message at 12 noon, or ealier in the morning to let people know who never got back to us that we're not coming?
                backgroundJob.Schedule<ISendRequestConfirmationMessagesTheDayOfAnItineraryDate>(x => x.SendSms(requestIds, itinerary.Id), Itinerary(itinerary.Date, itinerary.Event.TimeZoneId));
            }
        }

        private bool TodayIsOneDayBeforeThe(DateTime itineraryDate, string eventsTimeZoneId)
        {
            var timeZoneInfo = TimeZoneInfo.FindSystemTimeZoneById(eventsTimeZoneId);
            var utcOffset = timeZoneInfo.GetUtcOffset(itineraryDate);
            var intineraryDateConvertedToEventsTimeZone = new DateTimeOffset(itineraryDate, utcOffset);
            return (intineraryDateConvertedToEventsTimeZone.Date - DateTimeUtcNow().Date).TotalDays == 1;
        }

        private static DateTimeOffset Itinerary(DateTime itineraryDate, string eventsTimeZoneId)
        {
            var timeZoneInfo = TimeZoneInfo.FindSystemTimeZoneById(eventsTimeZoneId);
            var itineraryDateAtNoon = itineraryDate.Date.AddHours(12);
            var utcOffset = timeZoneInfo.GetUtcOffset(itineraryDateAtNoon);
            return new DateTimeOffset(itineraryDateAtNoon, utcOffset);
        }
    }

    public interface ISendRequestConfirmationMessagesADayBeforeAnItineraryDate
    {
        void SendSms(List<Guid> requestIds, int itineraryId);
    }
}