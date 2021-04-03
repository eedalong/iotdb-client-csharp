using Thrift;
using Thrift.Transport;
using Thrift.Protocol;
using Thrift.Server;
using System;
using System.Linq;
using System.Net.Sockets;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Thrift.Collections;


using Thrift.Protocol.Entities;
using Thrift.Protocol.Utilities;
using Thrift.Transport.Client;
using Thrift.Transport.Server;
using Thrift.Processor;
using iotdb_client_csharp.client.utils;

namespace iotdb_client_csharp.client
{
    public enum TSDataType{BOOLEAN, INT32, INT64, FLOAT, DOUBLE, TEXT, NONE};
    public enum TSEncoding{PLAIN, PLAIN_DICTIONARY, RLE, DIFF, TS_2DIFF, BITMAP, GORILLA_V1, REGULAR, GORILLA, NONE};
    public enum Compressor{UNCOMPRESSED, SNAPPY, GZIP, LZO, SDT, PAA, PLA, LZ4};

    public class Session
    {
       private string username="root", password="root", zoneId, host;
       public int SUCCESS_CODE{
           get{return 200;}
       }
       private int port, fetch_size=10000;
       private long sessionId, statementId;
       private bool is_close = true;

       private TSIService.Client client; 
       private TSocketTransport transport;
       private static TSProtocolVersion protocol_version = TSProtocolVersion.IOTDB_SERVICE_PROTOCOL_V3;


       public Session(string host, int port){
           // init success code 
           this.host = host;
           this.port = port;
       } 
        public Session(string host, int port, string username="root", string password="root", int fetch_size=10000, string zoneId = "UTC+08:00"){
            this.host = host;
            this.port = port;
            this.username = username;
            this.password = password;
            this.zoneId = zoneId;
            this.fetch_size = fetch_size;
        }
        public void open(bool enableRPCCompression){
            if(!is_close){
                return ;
            }
            this.transport = new TSocketTransport(this.host, this.port, null);
            if(!transport.IsOpen){
                try{
                    var task = transport.OpenAsync(new CancellationToken());
                    task.Wait();
                }
                catch(TTransportException e){
                    //TODO, should define our own Exception
                    // here we just print the exception
                    Console.Write(e.ToString());
                    throw e;
                }
            }
            if(enableRPCCompression){
                client = new TSIService.Client(new TCompactProtocol(transport));
            }else{
                client = new TSIService.Client(new TBinaryProtocol(transport));
            }
            // init open request
            var open_req = new TSOpenSessionReq(protocol_version, zoneId);
            open_req.Username = username;
            open_req.Password = password;
            try{
                var task = client.openSessionAsync(open_req);
                task.Wait();
                var open_resp = task.Result;
                if(open_resp.ServerProtocolVersion != protocol_version){
                    var message = String.Format("Protocol Differ, Client version is {0} but Server version is {1}", protocol_version, open_resp.ServerProtocolVersion);
                    Console.WriteLine(message);
                }
                if (open_resp.ServerProtocolVersion == 0){
                    throw new TException("Protocol not supported", null);
                }
                sessionId = open_resp.SessionId;
                var statement_task = client.requestStatementIdAsync(sessionId);
                statement_task.Wait();
                statementId = statement_task.Result;
            }
            catch(Exception e){
                transport.Close();
                Console.WriteLine("session closed because ", e);
                throw e;
            }
            if(zoneId != ""){
                set_time_zone(zoneId);
            }else{
                zoneId = get_time_zone();
            } 
            is_close = false;          

        }
        public bool is_open(){
            return !is_close;
        }
        public void close(){
            if(is_close){
                return;
            }
            var req = new TSCloseSessionReq(sessionId);
            try{
                var task = client.closeSessionAsync(req);
                task.Wait();
            }
            catch(TException e){
                var message = String.Format("Error occurs when closing session at server. Maybe server is down. Error message:{0}", e);
                Console.WriteLine(message);
                throw e;
            }
            finally{
                is_close = true;
                if (transport != null){
                    transport.Close();
                }
            }

        }
        public int set_storage_group(string group_name){
            var task = client.setStorageGroupAsync(sessionId, group_name);
            task.Wait();
            var status = task.Result;
            return verify_success(status);
        }

        public int delete_storage_group(string group_name){
            var task = client.deleteStorageGroupsAsync(sessionId, new List<string>{group_name});
            task.Wait();
            var status = task.Result;
            return verify_success(status);
        }
        public int delete_storage_groups(List<string> group_names){
            var task = client.deleteStorageGroupsAsync(sessionId, group_names);
            task.Wait();
            var status = task.Result;
            var message = String.Format("delete storage group(s) {0} message: {1}", group_names, status.Message);
            Console.WriteLine(message);
            return verify_success(status);
        }

        public int create_time_series(string ts_path, TSDataType data_type, TSEncoding encoding, Compressor compressor){
            var req = new TSCreateTimeseriesReq(sessionId, ts_path, (int)data_type, (int)encoding, (int)compressor);
            var task = client.createTimeseriesAsync(req);
            task.Wait();
            var status = task.Result;
            var message = String.Format("creating time series {0} message: {1}", ts_path, status.Message);
            Console.WriteLine(message);
            return verify_success(status); 
        }

        public int create_multi_time_series(List<string> ts_path_lst, List<TSDataType> data_type_lst, List<TSEncoding> encoding_lst, List<Compressor> compressor_lst){
            var data_types = data_type_lst.ConvertAll<int>(x => (int)x);
            var encodings = encoding_lst.ConvertAll<int>(x => (int)x);
            var compressors = compressor_lst.ConvertAll<int>(x => (int)x);
            var req = new TSCreateMultiTimeseriesReq(sessionId, ts_path_lst, data_types, encodings, compressors);
            var task = client.createMultiTimeseriesAsync(req);
            task.Wait();
            var status = task.Result;
            var message = String.Format("creating multiple time series {0} message: {1}", ts_path_lst, status.Message);
            Console.WriteLine(message);
            return verify_success(status);
        }
        public int delete_time_series(List<string> path_list){
            var task = client.deleteTimeseriesAsync(sessionId, path_list);
            task.Wait();
            var status = task.Result;
            var message = String.Format("deleting multiple time series {0} message: {1}", path_list, status.Message);
            Console.WriteLine(message);
            return verify_success(status);
        }
        public bool check_time_series_exists(string ts_path){
            // TBD by dalong
            return false;
        }
        public int delete_data(List<string> ts_path_lst, long start_time, long end_time){
            var req = new TSDeleteDataReq(sessionId, ts_path_lst, start_time, end_time);
            TSStatus status;
            try{
                var task = client.deleteDataAsync(req);
                task.Wait();
                status = task.Result;
            }
            catch(TException e){
                var message_local = String.Format("data deletion fails because: {0}", e);
                Console.WriteLine(message_local);
                throw e;
            }
            var message = String.Format("delete data from {0}, message: {1}", ts_path_lst, status.Message);
            Console.WriteLine(message);
            return verify_success(status);
        }
        public int insert_str_record(string device_id, List<string> measurements, List<string> values, long timestamp){
            // TBD by Luzhan
            return 0;
        }
        public int insert_record(string device_id, List<string> measurements, List<string> values, List<TSDataType> data_types, long timestamp){
            // TBD by Luzhan
            return 0;
        }
        public int insert_records(List<string> device_id, List<List<string>> measurements_lst, List<List<string>> values_lst, List<List<TSDataType>> data_types_lst, List<long> timestamp_lst){
            // TBD by Luzhan
            return 0;
        }
        public int test_insert_record(string device_id, List<string> measurements, List<string> values, List<TSDataType> data_types, long timestamp){
            // TBD by Luzhan
            return 0;
        }
        public int test_insert_records(List<string> device_id, List<List<string>> measurements_lst, List<List<string>> values_lst, List<List<TSDataType>> data_types_lst, List<long> timestamp_lst){
            // TBD by Luzhan
            return 0;
        }
        public int insert_tablet(Tablet tablet){
            // TBD by Luzhan
            return 0;
        }
        public int insert_tablets(List<Tablet> tablet_lst){
            // TBD by Luzhan
            return 0;
        }
        public int insert_records_of_one_device(long device_id, List<long> timestamp_lst, List<List<string>> measurements_lst, List<List<TSDataType>> data_types_lst, List<List<string>> values_lst){
            var sorted = timestamp_lst.Select((x, index) => (timestamp: x, measurements:measurements_lst[index], data_types:data_types_lst[index], values:values_lst[index])).OrderBy(x => x.timestamp).ToList();
            List<long> sorted_timestamp_lst = sorted.Select(x => x.timestamp).ToList();
            List<List<string>> sorted_measurements_lst = sorted.Select(x => x.measurements).ToList();
            List<List<TSDataType>> sorted_datatye_lst = sorted.Select(x => x.data_types).ToList();
            List<List<string>> sorted_value_lst = sorted.Select(x => x.values).ToList();
            return insert_records_of_one_device_sorted(device_id, sorted_timestamp_lst, sorted_measurements_lst, sorted_datatye_lst, sorted_value_lst);

        }
        public int insert_records_of_one_device_sorted(long device_id, List<long> timestamp_lst, List<List<string>> measurements_lst, List<List<TSDataType>> data_types_lst, List<List<string>> values_lst){
            // TBD by Luzhan
            return 0;
        }

        public int test_insert_tablet(Tablet tablet){
            // TBD by Luzhan
            return 0;
        }
        public int test_insert_tablets(List<Tablet> tablet_lst){
            // TBD by Luzhan
            return 0;
        }

        private int verify_success(TSStatus status){
            if (status.Code == SUCCESS_CODE){
                return 0;
            }
            var message = String.Format("error status is {}", status);
            Console.WriteLine(message);
            return -1;
        }
         
        public void set_time_zone(string zoneId){
            var req = new TSSetTimeZoneReq(sessionId, zoneId);
            try{
                var task = client.setTimeZoneAsync(req);
                task.Wait();
                var message = String.Format("setting time zone_id as {0}, message:{1}", zoneId, task.Result.Message);
                Console.WriteLine(message);
            }
            catch(TException e ){
                var message = String.Format("could not set time zone because {0}", e);
                Console.WriteLine(message);
                throw e; 
            }
            this.zoneId = zoneId;
        }
        public string get_time_zone(){
            TSGetTimeZoneResp resp;
            if(zoneId != ""){
                return zoneId;
            }
            try{
                var task = client.getTimeZoneAsync(sessionId);
                task.Wait();
                resp = task.Result;
            }
            catch(TException e){
                var message = String.Format("counld not get time zone beacuse {0}", e);
                Console.WriteLine(message);
                throw e; 
            }
            return resp.TimeZone;
        }
       
    }
}