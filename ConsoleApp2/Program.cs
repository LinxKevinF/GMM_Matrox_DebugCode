//***************************************************************************************
//
// File name: MdigProcess.cs
//
// Synopsis:  This program shows the use of the MdigProcess() function and its multiple
//            buffering acquisition to do robust real-time processing.
//
//            The user's processing code to execute is located in a callback function 
//            that will be called for each frame acquired (see ProcessingFunction()).
//
//      Note: The average processing time must be shorter than the grab time or some
//            frames will be missed. Also, if the processing results are not displayed
//            and the frame count is not drawn or printed, the CPU usage is reduced 
//            significantly.
//
// Copyright © Matrox Electronic Systems Ltd., 1992-2021.
// All Rights Reserved
//***************************************************************************************
using System;
using System.Collections.Generic;
using System.Text;
using System.Runtime.InteropServices;

using Matrox.MatroxImagingLibrary;
using System.Threading;

namespace ConsoleApp2
{
    class Program
    {
        // Number of images in the buffering grab queue.
        // Generally, increasing this number gives a better real-time grab.
        private const int BUFFERING_SIZE_MAX = 2;
        static DateTime curTriggerTime;

        static DateTime GlobalTimerEndTime;
        static DateTime GlobalGrabFrameStartTime;
        static MIL_INT RetryTriggerCount = 0;

        static MIL_ID MilDigitizer = MIL.M_NULL;
        // User's processing function hook data object.
        public class HookDataStruct
        {
            public MIL_ID MilDigitizer;
            public MIL_ID MilImageDisp;
            public int ProcessedImageCount;
            public MIL_INT NbGrabStart;
            public DateTime GrabFrameStartTime;
            public DateTime GrabFrameEndTime;
            public DateTime TimerEndTime;
        };

        // Main function.
        static void Main(string[] args)
        {
            MIL_ID MilApplication = MIL.M_NULL;
            MIL_ID MilApplication2 = MIL.M_NULL;
            MIL_ID MilSystem = MIL.M_NULL;
            //MIL_ID MilDigitizer = MIL.M_NULL;
            MIL_ID MilDisplay = MIL.M_NULL;
            MIL_ID MilImageDisp = MIL.M_NULL;
            MIL_ID[] MilGrabBufferList = new MIL_ID[BUFFERING_SIZE_MAX];
            int MilGrabBufferListSize = 0;
            MIL_INT ProcessFrameCount = 0;
            double ProcessFrameRate = 0;
            MIL_INT ProcessMissFrameCount = 0;

            HookDataStruct UserHookData = new HookDataStruct();

            // Allocate defaults.
            //MIL.MappAlloc(MIL.M_NULL, MIL.M_DEFAULT, ref MilApplication);
            MIL.MappAlloc("M_DEFAULT", MIL.M_DEFAULT, ref MilApplication);
            MIL.MsysAlloc(MIL.M_DEFAULT, MIL.M_SYSTEM_DEFAULT, MIL.M_DEFAULT, MIL.M_DEFAULT, ref MilSystem);
            MIL.MdispAlloc(MilSystem, MIL.M_DEFAULT, "M_DEFAULT", MIL.M_WINDOWED, ref MilDisplay);
            MIL.MdigAlloc(MilSystem, MIL.M_DEFAULT, "M_DEFAULT", MIL.M_DEFAULT, ref MilDigitizer);

            MIL.MdigControl(MilDigitizer, MIL.M_GRAB_TIMEOUT, MIL.M_DISABLE);
            MIL.MdigControl(MilDigitizer, MIL.M_GRAB_MODE, MIL.M_ASYNCHRONOUS);
            // Allocate a monochrome display buffer. 
            MIL.MbufAlloc2d(MilSystem,
                MIL.MdigInquire(MilDigitizer, MIL.M_SIZE_X, MIL.M_NULL),
                MIL.MdigInquire(MilDigitizer, MIL.M_SIZE_Y, MIL.M_NULL),
                8 + MIL.M_UNSIGNED,
                MIL.M_IMAGE + MIL.M_GRAB + MIL.M_PROC + MIL.M_DISP,
                ref MilImageDisp);
            MIL.MbufClear(MilImageDisp, MIL.M_COLOR_BLACK);

            // Display the image buffer. 
            MIL.MdispSelect(MilDisplay, MilImageDisp);

            MIL.MdigControl(MilDigitizer, MIL.M_AUX_IO4 + MIL.M_IO_INTERRUPT_ACTIVATION, MIL.M_EDGE_RISING); // Specifies that an interrupt will be generated upon a low-to-high signal transition.
            MIL.MdigControl(MilDigitizer, MIL.M_AUX_IO4 + MIL.M_IO_INTERRUPT_STATE, MIL.M_ENABLE); // Specifies not to generate an interrupt.
            MIL.MdigControl(MilDigitizer, MIL.M_AUX_IO4 + MIL.M_IO_MODE, MIL.M_OUTPUT); // Specifies that the signal is for output.
            MIL.MdigControl(MilDigitizer, MIL.M_AUX_IO4 + MIL.M_IO_SOURCE, MIL.M_TIMER1); // Specifies to route the output of timer n , where n is the number of timers available.

            // Print a message.
            Console.WriteLine();
            Console.WriteLine("MULTIPLE BUFFERED PROCESSING.");
            Console.WriteLine("-----------------------------");
            Console.WriteLine();
            Console.WriteLine("Press <Enter> to start processing.");
            Console.WriteLine();

            // Grab continuously on the display and wait for a key press.
            //MIL.MdigGrabContinuous(MilDigitizer, MilImageDisp);
            //Console.ReadKey();

            //// Halt continuous grab.
            //MIL.MdigHalt(MilDigitizer);

            // Allocate the grab buffers and clear them.
            MIL.MappControl(MIL.M_DEFAULT, MIL.M_ERROR, MIL.M_PRINT_ENABLE);
            for (MilGrabBufferListSize = 0; MilGrabBufferListSize < BUFFERING_SIZE_MAX; MilGrabBufferListSize++)
            {
                MIL.MbufAlloc2d(MilSystem,
                                MIL.MdigInquire(MilDigitizer, MIL.M_SIZE_X, MIL.M_NULL),
                                MIL.MdigInquire(MilDigitizer, MIL.M_SIZE_Y, MIL.M_NULL),
                                8 + MIL.M_UNSIGNED,
                                MIL.M_IMAGE + MIL.M_GRAB + MIL.M_PROC,
                                ref MilGrabBufferList[MilGrabBufferListSize]);

                if (MilGrabBufferList[MilGrabBufferListSize] != MIL.M_NULL)
                {
                    MIL.MbufClear(MilGrabBufferList[MilGrabBufferListSize], 0xFF);
                }
                else
                {
                    break;
                }
            }
            MIL.MappControl(MIL.M_DEFAULT, MIL.M_ERROR, MIL.M_PRINT_ENABLE);
            
            // Initialize the user's processing function data structure.
            UserHookData.MilDigitizer = MilDigitizer;
            UserHookData.MilImageDisp = MilImageDisp;
            UserHookData.ProcessedImageCount = 0;

            // get a handle to the HookDataStruct object in the managed heap, we will use this 
            // handle to get the object back in the callback function
            GCHandle hUserData = GCHandle.Alloc(UserHookData);

            MIL_DIG_HOOK_FUNCTION_PTR grabStartFrameDelegate = new MIL_DIG_HOOK_FUNCTION_PTR(GrabStart);
            MIL.MdigHookFunction(MilDigitizer, MIL.M_GRAB_START, grabStartFrameDelegate, GCHandle.ToIntPtr(hUserData));

            MIL_DIG_HOOK_FUNCTION_PTR grabStopFrameDelegate = new MIL_DIG_HOOK_FUNCTION_PTR(GrabFrameStart);
            MIL.MdigHookFunction(MilDigitizer, MIL.M_GRAB_FRAME_START, grabStopFrameDelegate, GCHandle.ToIntPtr(hUserData));

            MIL_DIG_HOOK_FUNCTION_PTR grabStartDelegate = new MIL_DIG_HOOK_FUNCTION_PTR(GrabFrameEnd);
            MIL.MdigHookFunction(MilDigitizer, MIL.M_GRAB_FRAME_END, grabStartDelegate, GCHandle.ToIntPtr(hUserData));

            MIL_DIG_HOOK_FUNCTION_PTR timerStartDelegate = new MIL_DIG_HOOK_FUNCTION_PTR(TimerStart);
            MIL.MdigHookFunction(MilDigitizer, MIL.M_TIMER_START, timerStartDelegate, GCHandle.ToIntPtr(hUserData));

            MIL_DIG_HOOK_FUNCTION_PTR timerEndDelegate = new MIL_DIG_HOOK_FUNCTION_PTR(TimerEnd);
            MIL.MdigHookFunction(MilDigitizer, MIL.M_TIMER_END, timerEndDelegate, GCHandle.ToIntPtr(hUserData));

            MIL_DIG_HOOK_FUNCTION_PTR TLTriggerCallbackPtr = new MIL_DIG_HOOK_FUNCTION_PTR(TLTriggerCallback);
            MIL.MdigHookFunction(MilDigitizer, MIL.M_TL_TRIGGER, TLTriggerCallbackPtr, IntPtr.Zero);

            MIL_DIG_HOOK_FUNCTION_PTR AuxIO4CallbackPtr = new MIL_DIG_HOOK_FUNCTION_PTR(AuxIO4Callback);
            MIL.MdigHookFunction(MilDigitizer, MIL.M_IO_CHANGE, AuxIO4CallbackPtr, IntPtr.Zero);

            

             MIL_DIG_HOOK_FUNCTION_PTR ProcessingFunctionPtr = new MIL_DIG_HOOK_FUNCTION_PTR(ProcessingFunction);

            // Start the processing. The processing function is called with every frame grabbed.
            MIL.MdigProcess(MilDigitizer, MilGrabBufferList, MilGrabBufferListSize, MIL.M_START, MIL.M_DEFAULT, ProcessingFunctionPtr, GCHandle.ToIntPtr(hUserData));
           
            // Issues a software trigger for the specified timer.
            //MIL.MdigControl(MilDigitizer, MIL.M_TIMER_TRIGGER_SOFTWARE + MIL.M_TIMER1, 1);
            curTriggerTime = DateTime.Now;
           // SendTrigger(MilDigitizer);

            // Here the main() is free to perform other tasks while the processing is executing.
            // ---------------------------------------------------------------------------------

            // Print a message and wait for a key press after a minimum number of frames.
            Console.WriteLine("Press <Enter> to stop.                    ");
            Console.WriteLine();
            Console.ReadKey();


            //while (true)
            //{
            //    Console.WriteLine("sending software trigger...");
            //    MIL.MdigControl(MilDigitizer, MIL.M_TIMER_TRIGGER_SOFTWARE + MIL.M_TIMER1, 1);

            //    Thread.Sleep(1000);

            //}

            // Stop the processing.
            MIL.MdigProcess(MilDigitizer, MilGrabBufferList, MilGrabBufferListSize, MIL.M_STOP, MIL.M_DEFAULT, ProcessingFunctionPtr, GCHandle.ToIntPtr(hUserData));

            // Free the GCHandle when no longer used
            hUserData.Free();

            // Print statistics.
            MIL.MdigInquire(MilDigitizer, MIL.M_PROCESS_FRAME_COUNT, ref ProcessFrameCount);
            MIL.MdigInquire(MilDigitizer, MIL.M_PROCESS_FRAME_RATE, ref ProcessFrameRate);
            MIL.MdigInquire(MilDigitizer, MIL.M_PROCESS_FRAME_MISSED, ref ProcessMissFrameCount);
            Console.WriteLine();
            Console.WriteLine();
            Console.WriteLine("{0} frames grabbed at {1:0.0} frames/sec ({2:0.0} ms/frame).", ProcessFrameCount, ProcessFrameRate, 1000.0 / ProcessFrameRate);
            Console.WriteLine("{0} ProcessMissFrameCount).", ProcessMissFrameCount);
            Console.WriteLine("Press <Enter> to end.");
            Console.WriteLine();
            Console.ReadKey();

            // Free the grab buffers.
            while (MilGrabBufferListSize > 0)
            {
                MIL.MbufFree(MilGrabBufferList[--MilGrabBufferListSize]);
            }

            // Free display buffer.
            MIL.MbufFree(MilImageDisp);

            MIL.MdigFree(MilDigitizer);
            MIL.MdispFree(MilDisplay);
            MIL.MsysFree(MilSystem);
            MIL.MappFree(MilApplication);

            // Release defaults.
            //MIL.MappFreeDefault(MilApplication, MilSystem, MilDisplay, MilDigitizer, MIL.M_NULL);


        }

        // User's processing function called every time a grab buffer is ready.
        // -----------------------------------------------------------------------

        // Local defines.
        private const int STRING_LENGTH_MAX = 20;
        private const int STRING_POS_X = 20;
        private const int STRING_POS_Y = 20;
        static MIL_INT ProcessingFunction(MIL_INT HookType, MIL_ID HookId, IntPtr HookDataPtr)
        {
            MIL_ID ModifiedBufferId = MIL.M_NULL;

            // this is how to check if the user data is null, the IntPtr class
            // contains a member, Zero, which exists solely for this purpose
            if (!IntPtr.Zero.Equals(HookDataPtr))
            {
                DateTime ProcessStartTime = DateTime.Now;


                // get the handle to the DigHookUserData object back from the IntPtr
                GCHandle hUserData = GCHandle.FromIntPtr(HookDataPtr);

                // get a reference to the DigHookUserData object
                HookDataStruct UserData = hUserData.Target as HookDataStruct;

                // Retrieve the MIL_ID of the grabbed buffer.
                MIL.MdigGetHookInfo(HookId, MIL.M_MODIFIED_BUFFER + MIL.M_BUFFER_ID, ref ModifiedBufferId);

                // Increment the frame counter.
                UserData.ProcessedImageCount++;


                MIL_INT TLTriggerCountIn = MIL.M_NULL;

                // Print and draw the frame count (remove to reduce CPU usage).
                Console.Write("Processing frame #{0}.\n", UserData.ProcessedImageCount);
                //MIL.MgraText(MIL.M_DEFAULT, ModifiedBufferId, STRING_POS_X, STRING_POS_Y, String.Format("{0}", UserData.ProcessedImageCount));

                //// Execute the processing and update the display.
                MIL.MimArith(ModifiedBufferId, MIL.M_NULL, UserData.MilImageDisp, MIL.M_NOT);

                //MIL_INT SrcImageDataPtr = MIL.M_NULL;
                //MIL_INT SrcImagePitchByte = 0;
                //MIL.MbufInquire(ModifiedBufferId, MIL.M_HOST_ADDRESS, ref SrcImageDataPtr);
                //MIL.MbufInquire(ModifiedBufferId, MIL.M_PITCH_BYTE, ref SrcImagePitchByte);
                //unsafe
                //{
                //    IntPtr SrcImageDataPtrIntPtr = SrcImageDataPtr;
                //    byte* SrcImageDataAddr = (byte*)SrcImageDataPtrIntPtr;
                //    Console.WriteLine("\nTest Value: {0}\n", (MIL_INT)SrcImageDataAddr[50]);
                //}


                DateTime TriggerTime = DateTime.Now;
                DateTime ProcessStopTime = DateTime.Now;
                SendTrigger(UserData.MilDigitizer);
    

                TimeSpan DiffTriggerTime = TriggerTime - curTriggerTime;
                TimeSpan DiffProcessTime = ProcessStopTime - ProcessStartTime;
                Console.Write("DiffTriggerTime #{0}\n", DiffTriggerTime.TotalMilliseconds);
                Console.Write("DiffProcessTime #{0}\n", DiffProcessTime.TotalMilliseconds);
                curTriggerTime = TriggerTime;
                Console.Write("TriggerTime #{0}.{1}\n", TriggerTime.Second, TriggerTime.Millisecond);
            }

            return 0;
        }

        static MIL_INT TriggerCount = 1;
        private static void SendTrigger(MIL_ID Digitizer)
        {
            MIL.MdigControl(Digitizer, MIL.M_TIMER_TRIGGER_SOFTWARE + MIL.M_TIMER1, 1);
            Console.WriteLine("Send Trigger #{0}", TriggerCount++);
        }

        private static MIL_INT GrabStart(MIL_INT HookType, MIL_ID EventId, IntPtr UserObjectPtr)
        {
            if (UserObjectPtr != IntPtr.Zero)
            {
                GCHandle userObjectHandle = GCHandle.FromIntPtr(UserObjectPtr);
                HookDataStruct userData = userObjectHandle.Target as HookDataStruct;
                if (userData != null)
                {
                    // Increment grab start count and print it.
                    userData.NbGrabStart++;
                    Console.Write("\nHookFunction #{0}\n", userData.NbGrabStart);

                    DateTime GrabStartTime = DateTime.Now;

                    Console.Write("GrabStartTime #{0}.{1}\n", GrabStartTime.Second, GrabStartTime.Millisecond);
                }
            }

            return (0);
        }

        private static MIL_INT GrabFrameStart(MIL_INT HookType, MIL_ID EventId, IntPtr UserObjectPtr)
        {
            if (UserObjectPtr != IntPtr.Zero)
            {
                GCHandle userObjectHandle = GCHandle.FromIntPtr(UserObjectPtr);
                HookDataStruct userData = userObjectHandle.Target as HookDataStruct;
                if (userData != null)
                {
                    // Increment grab start count and print it.
                    DateTime GrabFrameStartTime = DateTime.Now;
                   
                    
                    userData.GrabFrameStartTime = GrabFrameStartTime;
                    GlobalGrabFrameStartTime = GrabFrameStartTime;
                    Console.Write("GrabFrameStartTime #{0}.{1}\n", userData.GrabFrameStartTime.Second,
                        userData.GrabFrameStartTime.Millisecond);
                }
            }

            return (0);
        }

        private static MIL_INT GrabFrameEnd(MIL_INT HookType, MIL_ID EventId, IntPtr UserObjectPtr)
        {
            if (UserObjectPtr != IntPtr.Zero)
            {
                GCHandle userObjectHandle = GCHandle.FromIntPtr(UserObjectPtr);
                HookDataStruct userData = userObjectHandle.Target as HookDataStruct;
                if (userData != null)
                {
                    // Increment grab start count and print it.
                    DateTime GrabFrameStopTime = DateTime.Now;
                    userData.GrabFrameEndTime = GrabFrameStopTime;

                    Console.Write("GrabFrameEndTime #{0}.{1}\n", userData.GrabFrameEndTime.Second, userData.GrabFrameEndTime.Millisecond);
                }
            }

            return (0);
        }

        private static MIL_INT TimerStart(MIL_INT HookType, MIL_ID EventId, IntPtr UserObjectPtr)
        {
            if (UserObjectPtr != IntPtr.Zero)
            {
                GCHandle userObjectHandle = GCHandle.FromIntPtr(UserObjectPtr);
                HookDataStruct userData = userObjectHandle.Target as HookDataStruct;
                if (userData != null)
                {
                    // Increment grab start count and print it.
                    DateTime TimerStartTime = DateTime.Now;

                    Console.Write("TimerStartTime #{0}.{1}\n", TimerStartTime.Second, TimerStartTime.Millisecond);
                }
            }
            //Thread.Sleep(8);
            //System.Environment.Exit(0);
            return (0);
        }

        private static MIL_INT TimerEnd(MIL_INT HookType, MIL_ID EventId, IntPtr UserObjectPtr)
        {
            if (UserObjectPtr != IntPtr.Zero)
            {
                GCHandle userObjectHandle = GCHandle.FromIntPtr(UserObjectPtr);
                HookDataStruct userData = userObjectHandle.Target as HookDataStruct;
                if (userData != null)
                {
                    // Increment grab start count and print it.
                    DateTime TimerEndTime = DateTime.Now;
                    userData.TimerEndTime = TimerEndTime;
                    GlobalTimerEndTime = TimerEndTime;
                    Console.Write("TimerEndTime #{0}.{1}\n", TimerEndTime.Second, TimerEndTime.Millisecond);
                }
                //Thread.Sleep(1);
                //System.Environment.Exit(0);
                //Thread RetryTriggerThread = new Thread(RetrySendTrigger);
                //RetryTriggerThread.Start();
            }

            return (0);
        }

        private static MIL_INT TLTriggerCallback(MIL_INT HookType, MIL_ID HookId, IntPtr HookDataPtr)
        {
            DateTime TLTriggerTime = DateTime.Now;

            Console.Write("TLTriggerTime #{0}.{1}\n", TLTriggerTime.Second, TLTriggerTime.Millisecond);
           
            return 0;
        }

        static MIL_INT AuxIO4Callback(MIL_INT HookType, MIL_ID HookId, IntPtr HookDataPtr)
        {
            DateTime AuxIO4Time = DateTime.Now;

            //Console.Write("AuxIO4Time #{0}.{1}\n", AuxIO4Time.Second, AuxIO4Time.Millisecond);
            return 0;
        }

        public static void RetrySendTrigger()
        {
            Console.WriteLine("~~~~~~~~~~~~~~Rerty Send Trigger #{0}", RetryTriggerCount);
            Thread.Sleep(20);
            TimeSpan ExplosurTime = GlobalGrabFrameStartTime - GlobalTimerEndTime;
            if (GlobalGrabFrameStartTime < GlobalTimerEndTime)
            {
                RetryTriggerCount++;
                SendTrigger(MilDigitizer);
                Console.WriteLine("~~~~~~~~~~~~~~Rerty Send Trigger #{0}", RetryTriggerCount);

            }

        }
    }
}
