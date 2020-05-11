﻿// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Net;
using FluentValidation.Results;
using Hl7.Fhir.Model;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Health.Abstractions.Exceptions;
using Microsoft.Health.Fhir.Api.Features.ActionResults;
using Microsoft.Health.Fhir.Api.Features.Bundle;
using Microsoft.Health.Fhir.Api.Features.Exceptions;
using Microsoft.Health.Fhir.Api.Features.Filters;
using Microsoft.Health.Fhir.Api.UnitTests.Features.Context;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Validation;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Net.Http.Headers;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Api.UnitTests.Features.Filters
{
    public class OperationOutcomeExceptionFilterTests
    {
        private readonly ActionExecutedContext _context;
        private readonly IFhirRequestContextAccessor _fhirRequestContextAccessor = Substitute.For<IFhirRequestContextAccessor>();
        private readonly DefaultFhirRequestContext _fhirRequestContext = new DefaultFhirRequestContext();
        private readonly string _correlationId = Guid.NewGuid().ToString();

        public OperationOutcomeExceptionFilterTests()
        {
            _context = new ActionExecutedContext(
                new ActionContext(new DefaultHttpContext(), new RouteData(), new ActionDescriptor()),
                new List<IFilterMetadata>(),
                FilterTestsHelper.CreateMockFhirController());

            _fhirRequestContext.CorrelationId = _correlationId;
            _fhirRequestContextAccessor.FhirRequestContext.Returns(_fhirRequestContext);
        }

        [Fact]
        public void GivenAFhirBasedException_WhenExecutingAnAction_ThenTheResponseShouldBeAnOperationOutcome()
        {
            var filter = new OperationOutcomeExceptionFilterAttribute(_fhirRequestContextAccessor);

            _context.Exception = Substitute.For<FhirException>();

            filter.OnActionExecuted(_context);

            var result = _context.Result as OperationOutcomeResult;

            Assert.NotNull(result);
            Assert.Equal(_correlationId, result.Result.Id);
        }

        [Fact]
        public void GivenAResourceGoneExceptionException_WhenExecutingAnAction_ThenTheResponseShouldBeAnOperationOutcome()
        {
            var filter = new OperationOutcomeExceptionFilterAttribute(_fhirRequestContextAccessor);

            _context.Exception = new ResourceGoneException(new ResourceKey<Observation>("id1", "version2"));

            filter.OnActionExecuted(_context);

            var result = _context.Result as OperationOutcomeResult;

            Assert.NotNull(result);
            Assert.Equal(HttpStatusCode.Gone, result.StatusCode);
            Assert.Equal("W/\"version2\"", result.Headers[HeaderNames.ETag]);

            var operation = result.Result;
            Assert.NotNull(operation);
            Assert.Equal(_correlationId, result.Result.Id);
        }

        [Fact]
        public void GivenAResourceNotFoundExceptionException_WhenExecutingAnAction_ThenTheResponseShouldBeAnOperationOutcome()
        {
            ValidateOperationOutcome(new ResourceNotFoundException("Exception"), HttpStatusCode.NotFound);
        }

        [Fact]
        public void GivenAFhirBasedExceptionWithIssueMessage_WhenExecutingAnAction_ThenTheResponseShouldBeAnOperationOutcomeWithIssueComponent()
        {
            var reason = "This is a test reason.";

            var operation = ValidateOperationOutcome(
                Substitute.For<FhirException>(new OperationOutcomeIssue(
                    OperationOutcomeConstants.IssueSeverity.Error,
                    OperationOutcomeConstants.IssueType.Invalid,
                    reason)),
                HttpStatusCode.BadRequest).Result;

            Assert.Equal(reason, operation.Issue[0].Diagnostics);
        }

        [Fact]
        public void GivenAResourceNotValidExceptionException_WhenExecutingAnAction_ThenTheResponseShouldBeAnOperationOutcome()
        {
            var propertyName = "test";
            var reason = "This is a test reason.";

            OperationOutcome operation = ValidateOperationOutcome(
                new ResourceNotValidException(new List<ValidationFailure>
                {
                    new ValidationFailure(propertyName, reason),
                }),
                HttpStatusCode.BadRequest).Result;

            Assert.Equal(reason, operation.Issue[0].Diagnostics);
        }

        [Fact]
        public void GivenAnUnsupportedMediaExceptionException_WhenExecutingAnAction_ThenTheResponseShouldBeAnOperationOutcome()
        {
            ValidateOperationOutcome(new UnsupportedMediaTypeException("Unsupported Media"), HttpStatusCode.UnsupportedMediaType);
        }

        [Fact]
        public void GivenAnInvalidSearchOperationException_WhenExecutingAnAction_ThenTheResponseShouldBeAnOperationOutcome()
        {
            ValidateOperationOutcome(new InvalidSearchOperationException("Invalid search parameter."), HttpStatusCode.Forbidden);
        }

        [Fact]
        public void GivenASearchOperationNotSupportedException_WhenExecutingAnAction_ThenTheResponseShouldBeAnOperationOutcome()
        {
            ValidateOperationOutcome(new SearchOperationNotSupportedException("Not supported."), HttpStatusCode.Forbidden);
        }

        [Fact]
        public void GivenARequestRateExceededException_WhenExecutingAnAction_ThenTheResponseShouldBeAnOperationOutcome()
        {
            TimeSpan retryAfter = TimeSpan.FromSeconds(1.5);

            OperationOutcomeResult result = ValidateOperationOutcome(new RequestRateExceededException(retryAfter), (HttpStatusCode)429);

            Assert.Contains(
                result.Headers,
                h => string.Equals(h.Key, "x-ms-retry-after-ms", StringComparison.Ordinal) && string.Equals(h.Value, "1500", StringComparison.Ordinal));
        }

        [Fact]
        public void GivenARequestNotValidException_WhenExecutingAnAction_ThenTheResponseShouldBeAnOperationOutcome()
        {
            ValidateOperationOutcome(new RequestNotValidException("Invalid request."), HttpStatusCode.BadRequest);
        }

        [Fact]
        public void GivenAnOperationNotImplementedException_WhenExecutingAnAction_ThenTheResponseShouldBeAnOperationOutcome()
        {
            ValidateOperationOutcome(new OperationNotImplementedException("Not implemented."), HttpStatusCode.MethodNotAllowed);
        }

        [Fact]
        public void GivenAJobNotFoundException_WhenExecutingAnAction_ThenTheResponseShouldBeAnOperationOutcome()
        {
            ValidateOperationOutcome(new JobNotFoundException("Job not found."), HttpStatusCode.NotFound);
        }

        [Theory]
        [InlineData(HttpStatusCode.BadRequest)]
        [InlineData(HttpStatusCode.InternalServerError)]
        public void GivenAnOperationFailedException_WhenExecutingAnAction_ThenTheResponseShouldBeAnOperationOutcome(HttpStatusCode statusCode)
        {
            ValidateOperationOutcome(new OperationFailedException("Operation failed.", statusCode), statusCode);
        }

        [Theory]
        [InlineData(HttpStatusCode.BadRequest)]
        [InlineData(HttpStatusCode.PreconditionFailed)]
        [InlineData(HttpStatusCode.NotFound)]
        public void GivenATransactionFailedException_WhenExecutingAnAction_ThenTheResponseShouldBeAnOperationOutcome(HttpStatusCode statusCode)
        {
            ValidateOperationOutcome(new FhirTransactionFailedException("Transaction failed.", statusCode, new List<OperationOutcomeIssue>()), statusCode);
        }

        [Fact]
        public void GivenANotAcceptableException_WhenExecutingAnAction_ThenTheResponseShouldBeAnOperationOutcome()
        {
            ValidateOperationOutcome(new NotAcceptableException("Not acceptable."), HttpStatusCode.NotAcceptable);
        }

        [Fact]
        public void GivenARequestEntityTooLargeException_WhenExecutingAnAction_ThenTheResponseShouldBeAnOperationOutcome()
        {
            ValidateOperationOutcome(new RequestEntityTooLargeException(), HttpStatusCode.RequestEntityTooLarge);
        }

        [Fact]
        public void GivenCmkAadClientCredentialsGrantFailureException_WhenExecutingAnAction_ThenTheResponseShouldBeAnOperationOutcome()
        {
            ValidateOperationOutcome(new CmkAadClientCredentialsGrantFailureException(), HttpStatusCode.Forbidden);
        }

        [Fact]
        public void GivenCmkAadServiceUnavailableException_WhenExecutingAnAction_ThenTheResponseShouldBeAnOperationOutcome()
        {
            ValidateOperationOutcome(new CmkAadServiceUnavailableException(), HttpStatusCode.Forbidden);
        }

        [Fact]
        public void GivenCmkKeyVaultAuthenticationFailureException_WhenExecutingAnAction_ThenTheResponseShouldBeAnOperationOutcome()
        {
            ValidateOperationOutcome(new CmkKeyVaultAuthenticationFailureException(), HttpStatusCode.Forbidden);
        }

        [Fact]
        public void GivenCmkKeyVaultKeyNotFoundException_WhenExecutingAnAction_ThenTheResponseShouldBeAnOperationOutcome()
        {
            ValidateOperationOutcome(new CmkKeyVaultKeyNotFoundException(), HttpStatusCode.Forbidden);
        }

        [Fact]
        public void GivenCmkKeyVaultServiceUnavailableException_WhenExecutingAnAction_ThenTheResponseShouldBeAnOperationOutcome()
        {
            ValidateOperationOutcome(new CmkKeyVaultServiceUnavailableException(), HttpStatusCode.Forbidden);
        }

        [Fact]
        public void GivenCmkKeyVaultWrapUnwrapFailureException_WhenExecutingAnAction_ThenTheResponseShouldBeAnOperationOutcome()
        {
            ValidateOperationOutcome(new CmkKeyVaultWrapUnwrapFailureException(), HttpStatusCode.Forbidden);
        }

        [Fact]
        public void GivenCmkInvalidKeyVaultKeyUriException_WhenExecutingAnAction_ThenTheResponseShouldBeAnOperationOutcome()
        {
            ValidateOperationOutcome(new CmkInvalidKeyVaultKeyUriException(), HttpStatusCode.Forbidden);
        }

        [Fact]
        public void GivenCmkInvalidInputBytesException_WhenExecutingAnAction_ThenTheResponseShouldBeAnOperationOutcome()
        {
            ValidateOperationOutcome(new CmkInvalidInputBytesException(), HttpStatusCode.Forbidden);
        }

        [Fact]
        public void GivenCmkKeyVaultInternalServerErrorException_WhenExecutingAnAction_ThenTheResponseShouldBeAnOperationOutcome()
        {
            ValidateOperationOutcome(new CmkKeyVaultInternalServerErrorException(), HttpStatusCode.Forbidden);
        }

        [Fact]
        public void GivenCmkKeyVaultDnsNotResolvedException_WhenExecutingAnAction_ThenTheResponseShouldBeAnOperationOutcome()
        {
            ValidateOperationOutcome(new CmkKeyVaultDnsNotResolvedException(), HttpStatusCode.Forbidden);
        }

        [Fact]
        public void GivenABundleEntryLimitExceededException_WhenExecutingAnAction_ThenTheResponseShouldBeAnOperationOutcome()
        {
            ValidateOperationOutcome(new BundleEntryLimitExceededException("Bundle entry limit exceeded."), HttpStatusCode.BadRequest);
        }

        [Fact]
        public void GivenATransactionFailedExceptionException_WhenExecutingAnAction_ThenTheResponseShouldBeAnOperationOutcome()
        {
            ValidateOperationOutcome(new TransactionFailedException(), HttpStatusCode.InternalServerError);
        }

        private OperationOutcomeResult ValidateOperationOutcome(Exception exception, HttpStatusCode expectedStatusCode)
        {
            var filter = new OperationOutcomeExceptionFilterAttribute(_fhirRequestContextAccessor);

            _context.Exception = exception;

            filter.OnActionExecuted(_context);

            var result = _context.Result as OperationOutcomeResult;

            Assert.NotNull(result);
            Assert.Equal(expectedStatusCode, result.StatusCode);
            Assert.Equal(_correlationId, result.Result.Id);

            Assert.IsType<OperationOutcomeResult>(result);

            return result;
        }
    }
}
