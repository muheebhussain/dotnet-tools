// src/models/metric.model.js
export interface Metric {
  clientEscalationsKyc: number;
  supplierEscalationsSourcing: number;
  gAndEPreclearanceRequests: number;
  fspEscalationsKyc: number;
  offerOfEmploymentEscalationsHr: number;
  spEscalationsOutsideSpeakers: number;
  eCommunicationEscalationsCommsSurveillance: number;
  obaPiCentralCompliance: number;
  gAndEExpensesDfin: number;
  gAndEExpenseAnomalies: number;
  agedGeReconciliations: number;
  supplierEscalationsTprm: number;
  descriptionUpdate: string;
}
import React from "react";
import { useForm } from "react-hook-form";
import axios from "axios";

const MetricForm = () => {
  const {
    register,
    handleSubmit,
    formState: { errors },
  } = useForm();

  const onSubmit = (data) => {
    axios
      .post("https://api.example.com/submit-metric", data) // Replace with your API URL
      .then((response) => {
        console.log("Metric submitted successfully", response.data);
      })
      .catch((error) => {
        console.error("Error submitting metric", error);
      });
  };

  return (
    <form onSubmit={handleSubmit(onSubmit)}>
      <div>
        <label>Client Escalations - KYC</label>
        <input
          type="number"
          {...register("clientEscalationsKyc", { required: true })}
        />
        {errors.clientEscalationsKyc && <p>This field is required</p>}
      </div>

      <div>
        <label>Supplier Escalations (Sourcing)</label>
        <input
          type="number"
          {...register("supplierEscalationsSourcing", { required: true })}
        />
        {errors.supplierEscalationsSourcing && <p>This field is required</p>}
      </div>

      <div>
        <label>G&E Preclearance Requests</label>
        <input
          type="number"
          {...register("gAndEPreclearanceRequests", { required: true })}
        />
        {errors.gAndEPreclearanceRequests && <p>This field is required</p>}
      </div>

      <div>
        <label>FSP Escalations - KYC</label>
        <input
          type="number"
          {...register("fspEscalationsKyc", { required: true })}
        />
        {errors.fspEscalationsKyc && <p>This field is required</p>}
      </div>

      <div>
        <label>Offer of Employment Escalations - HR</label>
        <input
          type="number"
          {...register("offerOfEmploymentEscalationsHr", { required: true })}
        />
        {errors.offerOfEmploymentEscalationsHr && <p>This field is required</p>}
      </div>

      <div>
        <label>S&P Escalations and Outside Speakers</label>
        <input
          type="number"
          {...register("spEscalationsOutsideSpeakers", { required: true })}
        />
        {errors.spEscalationsOutsideSpeakers && <p>This field is required</p>}
      </div>

      <div>
        <label>E-Communication Escalations - COMMS Surveillance</label>
        <input
          type="number"
          {...register("eCommunicationEscalationsCommsSurveillance", {
            required: true,
          })}
        />
        {errors.eCommunicationEscalationsCommsSurveillance && (
          <p>This field is required</p>
        )}
      </div>

      <div>
        <label>OBA/PI - Central Compliance</label>
        <input
          type="number"
          {...register("obaPiCentralCompliance", { required: true })}
        />
        {errors.obaPiCentralCompliance && <p>This field is required</p>}
      </div>

      <div>
        <label>G&E Expenses - DFIN</label>
        <input
          type="number"
          {...register("gAndEExpensesDfin", { required: true })}
        />
        {errors.gAndEExpensesDfin && <p>This field is required</p>}
      </div>

      <div>
        <label>G&E Expense Anomalies</label>
        <input
          type="number"
          {...register("gAndEExpenseAnomalies", { required: true })}
        />
        {errors.gAndEExpenseAnomalies && <p>This field is required</p>}
      </div>

      <div>
        <label>Aged G&E Reconciliations</label>
        <input
          type="number"
          {...register("agedGeReconciliations", { required: true })}
        />
        {errors.agedGeReconciliations && <p>This field is required</p>}
      </div>

      <div>
        <label>Supplier Escalations (TPRMs)</label>
        <input
          type="number"
          {...register("supplierEscalationsTprm", { required: true })}
        />
        {errors.supplierEscalationsTprm && <p>This field is required</p>}
      </div>

      <div>
        <label>Description Update</label>
        <textarea
          {...register("descriptionUpdate", { required: true })}
        ></textarea>
        {errors.descriptionUpdate && <p>This field is required</p>}
      </div>

      <button type="submit">Submit</button>
    </form>
  );
};

export default MetricForm;
