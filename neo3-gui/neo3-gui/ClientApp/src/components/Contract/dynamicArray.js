import React from 'react';
import 'antd/dist/antd.css';
import axios from 'axios';
import { withTranslation } from "react-i18next";
import { Form, Input, Button,Select,Row,Col } from 'antd';
import { MinusCircleOutlined, PlusOutlined } from '@ant-design/icons';


const {Option} = Select;
const formItemLayout = {
  labelCol: {
    xs: { span: 24 },
    sm: { span: 4 },
  },
  wrapperCol: {
    xs: { span: 24 },
    sm: { span: 20 },
  },
};

const formItemLayoutWithOutLabel = {
  wrapperCol: {
    xs: { span: 24, offset: 0 },
    sm: { span: 20, offset: 0 },
  },
};

const typeOption = [
"Signature",
"Boolean",
"Integer",
"Hash160",
"Hash256",
"ByteArray",
"PublicKey",
"String"
]


@withTranslation()
class DynamicArray extends React.Component{
  handleparam = values => {
    this.props.handleparam(values);
  }
  render = () =>{
    const { t } = this.props;
    return (
        <Form name="dynamic_form" {...formItemLayoutWithOutLabel} onFinish={this.handleparam}>
          <Form.List name="guiarray">
            {(fields, { add, remove }) => {
              return (
                  <div>
                  {fields.map((field) => (
                    <Row key={field.key}>
                      <Col span="8">
                      <Form.Item
                        name={[field.name, "type"]}
                        label={t("Type")}
                        rules={[
                        {
                          required: true,
                          message: t("wallet.please select a account"),
                        },
                        ]}
                      >
                        <Select
                        placeholder={t("select account")}
                        style={{ width: '100%' }}>
                        {typeOption.map((item) => {
                          return (
                          <Option key={item}>{item}</Option>
                          )
                        })}
                        </Select>
                      </Form.Item>
                      </Col>
                      <Col span="16">
                      <Form.Item
                          name={[field.name, "amount"]}
                          label={t("Array 类型")}
                          rules={[
                          {
                              required: true,
                              message: t("wallet.required"),
                          },
                          ]}>
                          <Input placeholder="JSON" />
                      </Form.Item>
                      </Col>
                      {fields.length > 1 ? (
                          <div className="delete-btn" onClick={ () => { remove(field.name); }}></div>
                      ) : null}
                    </Row>
                  ))}
                  <Form.Item className="mb0">
                    <Button
                      type="dashed"
                      onClick={() => {
                        add();
                      }}
                      style={{ width: "100%" }}
                    >
                      <PlusOutlined /> {t("wallet.transfer add")}
                    </Button>
                  </Form.Item>
                </div>
              );
            }}
          </Form.List>
    
          <Form.Item>
            <Button type="primary" htmlType="submit">
              构造
            </Button>
          </Form.Item>
        </Form>
        );
    }
} 


export default DynamicArray;