import axios from "axios";
import { message } from "antd";
import Config from "../config";

let count = 0;
const request = async (method, params) => {
  // 默认Axios Post方法
  const url = Config.RPCURL;
  if (method === "") {
    message.error("method null");
    return;
  }
  var options = Object.assign(
    {
      id: count,
      method: method,
    },
    { params: params }
  );
  count++;
  return await axios.post(url, options);
};

/**
 * 封装post请求
 * @param { String } method 请求方法
 * @param { Object } params 请求参数
 */
const post = (method, params) => {
  return request(method, params);
};

const postAsync = async (method, params) => {
  let response = await request(method, params);
  if (response.status !== 200) {
    let error = new Error("Response Error!");
    error.response = response;
    throw error;
  }
  return response.data;
}

export { post, postAsync };
export default request;
