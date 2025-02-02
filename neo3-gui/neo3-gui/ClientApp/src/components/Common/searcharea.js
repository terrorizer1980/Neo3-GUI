/* eslint-disable */
import React, { useState } from "react";
import "antd/dist/antd.css";
import { useHistory } from "react-router-dom";
import axios from "axios";
import { Input, message } from "antd";
import Topath from "../Common/topath";
import { post } from "../../core/request";
import { ArrowRightOutlined, SearchOutlined } from "@ant-design/icons";
import { withTranslation, useTranslation } from "react-i18next";

@withTranslation()
class Searcharea extends React.Component {
  constructor(props) {
    super(props);
    this.state = {
      size: "default",
      path: "",
      disabled: false,
      cname: "search-content",
    };
  }
  addClass = (e) => {
    this.stopPropagation(e);
    this.setState({
      cname: "search-content height-sea show-child",
      disabled: true,
    });
    document.addEventListener("click", this.removeClass);
  };
  removeClass = () => {
    if (this.state.disabled) {
      this.setState({
        cname: "search-content height-sea",
        disabled: false,
      });
    }
    document.removeEventListener("click", this.removeClass);
    setTimeout(
      () =>
        this.setState({
          cname: "search-content",
          disabled: false,
        }),
      500
    );
  };
  stopPropagation(e) {
    e.nativeEvent.stopImmediatePropagation();
  }
  searchContract = () => {
    const { t } = this.props;
    let _hash = this.refs.sinput.input.value.trim();
    if (!_hash) {
      message.info(t("search.check again"));
      return;
    }
    var _this = this;
    axios
      .post("http://localhost:8081", {
        id: "1111",
        method: "GetContract",
        params: {
          contractHash: _hash,
        },
      })
      .then(function (response) {
        var _data = response.data;
        if (_data.msgType === -1) {
          message.info(t("search.hash unexist"));
          return;
        } else if (_data.msgType === 3) {
          _this.setState({ topath: "/contract/detail:" + _hash });
        }
      })
      .catch(function (error) {
        console.log(error);
        console.log("error");
      });
  };
  render = () => {
    const { t } = this.props;
    return (
      <div className="search-area">
        <Topath topath={this.state.topath}></Topath>
        <div className="search-btn">
          <SearchOutlined className="inset-btn" onClick={this.addClass} />
        </div>
        <div className={this.state.cname}>
          <div
            className="search-detail"
            ref="sarea"
            onClick={this.stopPropagation}
          >
            <Input
              placeholder={t("search.hash-hint")}
              onPressEnter={this.searchContract}
              ref="sinput"
              suffix={<ArrowRightOutlined onClick={this.searchContract} />}
            />
          </div>
        </div>
      </div>
    );
  };
}

const Searchtttt = () => {
  // console.log(this.props)
  const { t } = useTranslation();
  const [hash, searchHash] = useState(false);
  let history = useHistory();
  const aaa = (value) => {
    console.log(value);
  };
  const sessss = () => {
    let txid =
      "0xc5c29246d1f1e05efffe541e6951ff60f6c5e03e2f246c1754c182604fad1105";
    console.log(txid);
    // let params = { "txId":txid };
    // post("GetTransaction",params).then(function (res) {
    //   var _data = res.data;
    //   if(_data.msgType === -1){
    //     message.info(t('search.hash unexist'));
    //     return;
    //   }else if(_data.msgType === 3){
    //     message.info("hashcunzai");
    //     history.push("/wallet/transaction:"+txid);
    //   }
    // })
    // .catch(function (error) {
    //   console.log(error);
    //   console.log("error");
    // });
  };
  return (
    <div>
      <SearchOutlined className="h2" onClick={aaa} />
      <div className="search-div">
        <Input
          placeholder={'t("search.hash-hint")'}
          onPressEnter={(value) => {
            console.log(value);
          }}
          suffix={<ArrowRightOutlined onClick={sessss} />}
        />
      </div>
    </div>
  );
};

export { Searchtttt };
export default Searcharea;
